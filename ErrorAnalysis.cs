using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace IndustrialFieldDataAcquisitionTerminal;

internal readonly record struct ErrorSample(double Reference, double Measured);
internal sealed record ErrorMetric(string Name, double? Value, string Unit, string Description);
internal sealed record ErrorAnalysisResult(IReadOnlyList<ErrorSample> Samples, IReadOnlyList<ErrorMetric> Metrics);
internal sealed record ErrorReportOptions(string ConstantReference, string FullScale, string TypeB,
    string CoverageFactor, string Unit, bool Repeated);

// Reference and measured values are paired observations of the same quantity.
internal static class ErrorAnalysis
{
    public static ErrorAnalysisResult Calculate(IReadOnlyList<ErrorSample> samples, double? fullScale,
        bool repeatedMeasurements, double? typeBStandard, double coverageFactor)
    {
        if (samples.Count == 0) throw new ArgumentException("请至少输入一组有效数据。");
        if (fullScale is <= 0 || fullScale is double f && !double.IsFinite(f))
            throw new ArgumentException("满量程跨度必须为大于零的有限数值。");
        if (typeBStandard is < 0 || typeBStandard is double b && !double.IsFinite(b))
            throw new ArgumentException("B 类标准不确定度必须为非负有限数值。");
        if (!double.IsFinite(coverageFactor) || coverageFactor <= 0)
            throw new ArgumentException("包含因子 k 必须为大于零的有限数值。");
        var n = samples.Count;
        var abs = new double[n];
        var errors = new double[n];
        double sumRef = 0, sumMeasured = 0, sumErr = 0, sumAbs = 0, sumSq = 0;
        double sumPct = 0, sumSignedPct = 0, sumSymmetricPct = 0, maxAbsPct = 0;
        double minErr = double.PositiveInfinity, maxErr = double.NegativeInfinity;
        double meanRef = 0, meanMeasured = 0, m2Ref = 0, m2Measured = 0, covariance = 0;
        int pctCount = 0, symmetricCount = 0;
        for (var i = 0; i < n; i++)
        {
            var (reference, measured) = samples[i];
            var error = measured - reference;
            if (!double.IsFinite(reference) || !double.IsFinite(measured) || !double.IsFinite(error))
                throw new ArgumentException($"第 {i + 1} 组包含非有限数值或数值超出计算范围。");
            var magnitude = Math.Abs(error);
            errors[i] = error; abs[i] = magnitude;
            sumRef += reference; sumMeasured += measured; sumErr += error; sumAbs += magnitude; sumSq += error * error;
            minErr = Math.Min(minErr, error); maxErr = Math.Max(maxErr, error);
            if (reference != 0)
            {
                var percent = error / reference * 100;
                if (!double.IsFinite(percent)) throw new ArgumentException($"第 {i + 1} 组的相对误差超出计算范围。");
                sumSignedPct += percent; sumPct += Math.Abs(percent); maxAbsPct = Math.Max(maxAbsPct, Math.Abs(percent)); pctCount++;
            }
            var denominator = Math.Abs(reference) + Math.Abs(measured);
            if (!double.IsFinite(denominator)) throw new ArgumentException($"第 {i + 1} 组的数值超出计算范围，请先缩放单位。");
            if (denominator > 0)
            { sumSymmetricPct += 200 * (magnitude / denominator); symmetricCount++; }
            var oldRef = meanRef; var oldMeasured = meanMeasured;
            meanRef += (reference - meanRef) / (i + 1);
            meanMeasured += (measured - meanMeasured) / (i + 1);
            m2Ref += (reference - oldRef) * (reference - meanRef);
            m2Measured += (measured - oldMeasured) * (measured - meanMeasured);
            covariance += (reference - oldRef) * (measured - meanMeasured);
        }
        if (!double.IsFinite(sumRef) || !double.IsFinite(sumMeasured) || !double.IsFinite(sumErr) ||
            !double.IsFinite(sumAbs) || !double.IsFinite(sumSq) || !double.IsFinite(sumPct) ||
            !double.IsFinite(sumSignedPct) || !double.IsFinite(sumSymmetricPct) ||
            !double.IsFinite(m2Ref) || !double.IsFinite(m2Measured) || !double.IsFinite(covariance))
            throw new ArgumentException("数据超出计算范围，请先适当缩放单位。");
        Array.Sort(abs);
        var bias = sumErr / n;
        var varianceErrors = n < 2 ? (double?)null : errors.Sum(x => (x - bias) * (x - bias)) / (n - 1);
        var errorStd = varianceErrors.HasValue ? Math.Sqrt(Math.Max(0, varianceErrors.Value)) : (double?)null;
        var measuredStd = n < 2 ? (double?)null : Math.Sqrt(Math.Max(0, m2Measured / (n - 1)));
        var sameReference = samples.All(x => x.Reference.Equals(samples[0].Reference));
        var typeA = repeatedMeasurements && sameReference && measuredStd.HasValue ? measuredStd / Math.Sqrt(n) : null;
        var combined = typeA.HasValue && typeBStandard.HasValue
            ? Math.Sqrt(typeA.Value * typeA.Value + typeBStandard.Value * typeBStandard.Value) : (double?)null;
        var maxAbsolute = abs[^1];
        var sortedErrors = errors.OrderBy(x => x).ToArray();
        var slope = n >= 2 && m2Ref > 0 ? covariance / m2Ref : (double?)null;
        var metrics = new List<ErrorMetric>();
        void Add(string name, double? value, string unit, string description) =>
            metrics.Add(new(name, value.HasValue && double.IsFinite(value.Value) ? value : null, unit, description));
        Add("样本数", n, "组", "参考值与测量值逐行配对。");
        Add("参考值平均", sumRef / n, "", "各组参考值的算术平均。");
        Add("测量值平均", sumMeasured / n, "", "各组测量值的算术平均。");
        Add("平均误差 / 偏差", bias, "", "平均(测量值－参考值)，保留正负号。");
        Add("最小有符号误差", minErr, "", "测量值－参考值的最小值。");
        Add("最大有符号误差", maxErr, "", "测量值－参考值的最大值。");
        Add("中位有符号误差", Quantile(sortedErrors, 0.5), "", "有符号误差的中位数。");
        Add("平均绝对误差 MAE", sumAbs / n, "", "平均|测量值－参考值|。");
        Add("均方误差 MSE", sumSq / n, "单位²", "平均(测量值－参考值)²。");
        Add("均方根误差 RMSE", Math.Sqrt(sumSq / n), "", "均方误差的平方根。");
        Add("最大绝对误差", maxAbsolute, "", "各组绝对误差的最大值。");
        Add("中位绝对误差", Quantile(abs, 0.5), "", "绝对误差的中位数。");
        Add("绝对误差 P95", Quantile(abs, 0.95), "", "绝对误差的 95% 分位数，线性插值。");
        Add("误差样本标准差", errorStd, "", "有符号误差的样本标准差，分母 n－1；至少 2 组。");
        Add("测量值样本标准差", measuredStd, "", "所有测量值的样本标准差；不同参考点混合时不能当作重复性。");
        Add("平均相对误差", pctCount > 0 ? sumSignedPct / pctCount : null, "%",
            $"平均[(测量值－参考值)/参考值]；参考值为零的 {n - pctCount} 组未参与。");
        Add("平均绝对百分比误差 MAPE", pctCount > 0 ? sumPct / pctCount : null, "%",
            $"平均[绝对误差/|参考值|]；参考值为零的 {n - pctCount} 组未参与。");
        Add("最大绝对相对误差", pctCount > 0 ? maxAbsPct : null, "%",
            $"最大[绝对误差/|参考值|]；参考值为零的 {n - pctCount} 组未参与。");
        Add("对称平均绝对百分比误差 sMAPE", symmetricCount > 0 ? sumSymmetricPct / symmetricCount : null, "%",
            $"平均[200×绝对误差/(|参考值|+|测量值|)]；双零的 {n - symmetricCount} 组未参与。");
        Add("最大引用误差", fullScale.HasValue ? 100 * maxAbsolute / fullScale.Value : null, "%",
            "最大绝对误差/满量程跨度×100%；需要输入正的跨度。");
        Add("决定系数 R²", n >= 2 && m2Ref > 0 ? 1 - sumSq / m2Ref : null, "—",
            "1－Σ(测量值－参考值)²/Σ(参考值－参考值均值)²；参考值恒定时不可计算，可为负数。");
        Add("皮尔逊相关系数 r", n >= 2 && m2Ref > 0 && m2Measured > 0 ? covariance / Math.Sqrt(m2Ref) / Math.Sqrt(m2Measured) : null, "—",
            "参考值与测量值的线性相关；任一组变量无变化时不可计算。");
        Add("线性拟合斜率 a", slope, "—", "最小二乘拟合：测量值 y = a×参考值 x + b；参考值需有变化。");
        Add("线性拟合截距 b", slope.HasValue ? meanMeasured - slope.Value * meanRef : null, "",
            "最小二乘拟合截距；测量值与参考值需逐行配对。");
        Add("重复测量变异系数 CV", repeatedMeasurements && sameReference && measuredStd.HasValue && meanMeasured != 0
            ? 100 * measuredStd.Value / Math.Abs(meanMeasured) : null, "%",
            "仅独立同条件且参考值一致的重复测量：样本标准差/|测量值均值|×100%。");
        Add("重复测量 A 类标准不确定度 uA", typeA, "",
            "仅勾选独立同条件重复测量、参考值一致且 n≥2 时，按测量值样本标准差/√n 估计。");
        Add("合成标准不确定度 uc", combined, "",
            "仅当 uA 与输入的 B 类标准不确定度均可用时，按独立分量平方和开根号；未包含其他未输入分量。");
        Add("扩展不确定度 U", combined.HasValue ? coverageFactor * combined.Value : null, "",
            $"U=k×uc，k={coverageFactor.ToString("G6", CultureInfo.InvariantCulture)}；覆盖概率依赖分布及其他条件。");
        return new ErrorAnalysisResult(samples.ToArray(), metrics);
    }

    private static double Quantile(double[] sorted, double p)
    {
        var index = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(index); var upper = (int)Math.Ceiling(index);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
    }
}

internal static class ErrorCsv
{
    public const int MaxRows = 50000;
    public static List<string[]> Read(string path)
    {
        try { return ReadEncoded(path, new UTF8Encoding(false, true)); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return ReadEncoded(path, Encoding.GetEncoding(936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
        }
    }
    private static List<string[]> ReadEncoded(string path, Encoding encoding)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: true);
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields() ?? Array.Empty<string>();
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(row);
            if (rows.Count > MaxRows + 100) throw new InvalidDataException($"CSV 超过 {MaxRows} 条数据，请拆分后导入。");
        }
        if (rows.Count == 0) throw new InvalidDataException("CSV 没有数据。");
        return rows;
    }
    public static string Field(string value) => value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
        ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static string MetricUnit(ErrorMetric metric, string unit) =>
        metric.Unit == "" ? unit : metric.Unit == "单位²" ? (unit.Length == 0 ? "单位²" : unit + "²") : metric.Unit;

    private static string Number(double? value) =>
        value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("G17", CultureInfo.InvariantCulture) : "";

    public static void WriteReport(string path, ErrorAnalysisResult result, ErrorReportOptions options)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(true)))
            {
                writer.WriteLine("类型,序号,指标,数值,单位,说明,参考值,测量值,有符号误差,绝对误差,相对误差%");
                void Write(params string[] fields) => writer.WriteLine(string.Join(",", fields.Select(Field)));
                Write("参数", "", "满量程跨度", options.FullScale, "", "");
                Write("参数", "", "统一参考值", options.ConstantReference, options.Unit, "");
                Write("参数", "", "B类标准不确定度", options.TypeB, "", "");
                Write("参数", "", "包含因子 k", options.CoverageFactor, "", "");
                Write("参数", "", "同条件独立重复测量", options.Repeated ? "是" : "否", "", "");
                foreach (var metric in result.Metrics)
                    Write("指标", "", metric.Name, Number(metric.Value), MetricUnit(metric, options.Unit), metric.Description);
                for (var i = 0; i < result.Samples.Count; i++)
                {
                    var sample = result.Samples[i]; var difference = sample.Measured - sample.Reference;
                    Write("数据", (i + 1).ToString(CultureInfo.InvariantCulture), "", "", "", "",
                        Number(sample.Reference), Number(sample.Measured), Number(difference),
                        Number(Math.Abs(difference)), Number(sample.Reference == 0 ? null : difference / sample.Reference * 100));
                }
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
