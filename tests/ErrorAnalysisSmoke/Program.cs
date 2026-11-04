using IndustrialFieldDataAcquisitionTerminal;
using System.Text;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static void Near(double? actual, double expected, string name) =>
    Check(actual.HasValue && Math.Abs(actual.Value - expected) < 1e-9, name + ": " + actual);
static double? Metric(ErrorAnalysisResult result, string name) => result.Metrics.Single(x => x.Name == name).Value;
var result = ErrorAnalysis.Calculate(new[] { new ErrorSample(10, 11), new ErrorSample(20, 18), new ErrorSample(30, 33) }, 100, false, null, 2);
Near(Metric(result, "样本数"), 3, "n");
Near(Metric(result, "平均误差 / 偏差"), 2.0 / 3, "bias");
Near(Metric(result, "平均绝对误差 MAE"), 2, "MAE");
Near(Metric(result, "均方误差 MSE"), 14.0 / 3, "MSE");
Near(Metric(result, "均方根误差 RMSE"), Math.Sqrt(14.0 / 3), "RMSE");
Near(Metric(result, "中位绝对误差"), 2, "median");
Near(Metric(result, "绝对误差 P95"), 2.9, "p95");
Near(Metric(result, "平均绝对百分比误差 MAPE"), 10, "MAPE");
Near(Metric(result, "最大引用误差"), 3, "full scale");
Near(Metric(result, "决定系数 R²"), 1 - 14.0 / 200, "R²");
Near(Metric(result, "线性拟合斜率 a"), 1.1, "regression slope");
Near(Metric(result, "线性拟合截距 b"), -4.0 / 3, "regression intercept");
Check(Metric(result, "重复测量 A 类标准不确定度 uA") is null, "uA needs explicit repeat assumption");
var zero = ErrorAnalysis.Calculate(new[] { new ErrorSample(0, 2), new ErrorSample(-10, -9) }, null, false, null, 2);
Near(Metric(zero, "平均相对误差"), -10, "negative reference signed relative error");
Near(Metric(zero, "平均绝对百分比误差 MAPE"), 10, "zero reference excluded");
Check(zero.Metrics.Single(x => x.Name == "平均绝对百分比误差 MAPE").Description.Contains("1 组"), "zero exclusion count");
var bothZero = ErrorAnalysis.Calculate(new[] { new ErrorSample(0, 0) }, null, false, null, 2);
Check(Metric(bothZero, "平均绝对百分比误差 MAPE") is null &&
      Metric(bothZero, "对称平均绝对百分比误差 sMAPE") is null &&
      Metric(bothZero, "误差样本标准差") is null && Metric(bothZero, "决定系数 R²") is null, "undefined cases");
var repeat = ErrorAnalysis.Calculate(new[] { new ErrorSample(10, 9), new ErrorSample(10, 10), new ErrorSample(10, 11) }, null, true, 0.4, 2);
Near(Metric(repeat, "重复测量 A 类标准不确定度 uA"), 1 / Math.Sqrt(3), "uA");
Near(Metric(repeat, "合成标准不确定度 uc"), Math.Sqrt(1.0 / 3 + 0.16), "uc");
Near(Metric(repeat, "扩展不确定度 U"), 2 * Math.Sqrt(1.0 / 3 + 0.16), "expanded");
Near(Metric(repeat, "重复测量变异系数 CV"), 10, "repeatability CV");
Check(Metric(repeat, "决定系数 R²") is null, "constant reference R² undefined");
Check(Metric(ErrorAnalysis.Calculate(new[] { new ErrorSample(1, 1), new ErrorSample(2, 2) }, null, true, 0.1, 2),
    "重复测量 A 类标准不确定度 uA") is null, "different references are not repeatability");
foreach (var invalid in new Action[] {
    () => ErrorAnalysis.Calculate(Array.Empty<ErrorSample>(), null, false, null, 2),
    () => ErrorAnalysis.Calculate(new[] { new ErrorSample(1, double.NaN) }, null, false, null, 2),
    () => ErrorAnalysis.Calculate(new[] { new ErrorSample(1, 2) }, 0, false, null, 2),
    () => ErrorAnalysis.Calculate(new[] { new ErrorSample(1, 2) }, null, true, -1, 2),
    () => ErrorAnalysis.Calculate(new[] { new ErrorSample(1, 2) }, null, true, 0, 0)
})
{
    var rejected = false; try { invalid(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "invalid input accepted");
}
var path = Path.Combine(Path.GetTempPath(), "daq-error-" + Guid.NewGuid().ToString("N") + ".csv");
try
{
    File.WriteAllText(path, "ref,measured,note\r\n1,2,\"quoted, field\"\r\n3,4,\"two\r\nlines\"\r\n", new UTF8Encoding(true));
    var rows = ErrorCsv.Read(path);
    Check(rows.Count == 3 && rows[1][2] == "quoted, field" && rows[2][2] == "two\r\nlines", "CSV quoting / multiline");
    Check(ErrorCsv.Field("a,\"b\"") == "\"a,\"\"b\"\"\"", "CSV escaping");
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    File.WriteAllText(path, "参考值,测量值\r\n10,10.5\r\n", Encoding.GetEncoding(936));
    rows = ErrorCsv.Read(path);
    Check(rows[0][0] == "参考值" && rows[1][1] == "10.5", "GBK CSV fallback");
    var precise = ErrorAnalysis.Calculate(new[] { new ErrorSample(Math.PI, Math.E) }, null, false, null, 2);
    ErrorCsv.WriteReport(path, precise, new ErrorReportOptions("", "", "", "2", "M,Pa", false));
    rows = ErrorCsv.Read(path);
    var data = rows.Single(x => x[0] == "数据");
    Check(double.Parse(data[6], System.Globalization.CultureInfo.InvariantCulture) == Math.PI &&
          double.Parse(data[7], System.Globalization.CultureInfo.InvariantCulture) == Math.E, "export preserves floating-point precision");
    Check(rows.Any(x => x[0] == "指标" && x[2] == "平均误差 / 偏差" && x[4] == "M,Pa"), "report metric units and escaping");
}
finally { File.Delete(path); }
Console.WriteLine("Error metrics, undefined cases, uncertainty assumptions and CSV parsing passed.");
