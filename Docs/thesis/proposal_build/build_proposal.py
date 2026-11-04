"""Build the local thesis proposal with native Word equations."""
from __future__ import annotations

import json
from pathlib import Path
from copy import deepcopy

from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT, WD_CELL_VERTICAL_ALIGNMENT
from docx.enum.section import WD_SECTION_START
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Cm, Pt, RGBColor
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT.parent / "数据采集时间统一与联合控制_开题报告.docx"
DATA = json.loads((ROOT / "content.json").read_text(encoding="utf-8"))
BLACK = RGBColor(0, 0, 0)


def font_style(style, size, chinese="宋体", latin="Times New Roman", bold=False):
    style.font.name = latin
    style.font.size = Pt(size)
    style.font.bold = bold
    style.font.italic = False
    style.font.underline = False
    style.font.color.rgb = BLACK
    props = style.element.get_or_add_rPr()
    fonts = props.find(qn("w:rFonts"))
    if fonts is None:
        fonts = OxmlElement("w:rFonts")
        props.insert(0, fonts)
    for key, value in (("ascii", latin), ("hAnsi", latin), ("eastAsia", chinese)):
        fonts.set(qn("w:" + key), value)
    for key in ("asciiTheme", "hAnsiTheme", "eastAsiaTheme", "cstheme"):
        fonts.attrib.pop(qn("w:" + key), None)


def run_font(run, size=None, bold=None):
    run.font.name = "Times New Roman"
    run.font.color.rgb = BLACK
    if size is not None:
        run.font.size = Pt(size)
    if bold is not None:
        run.font.bold = bold
    fonts = run._element.get_or_add_rPr().get_or_add_rFonts()
    fonts.set(qn("w:eastAsia"), "宋体")


def math_run(text):
    node = OxmlElement("m:r")
    props = OxmlElement("m:rPr")
    normal = OxmlElement("m:nor")
    props.append(normal)
    node.append(props)
    value = OxmlElement("m:t")
    value.text = text
    value.set(qn("xml:space"), "preserve")
    node.append(value)
    return node


def sub(base, suffix):
    node = OxmlElement("m:sSub")
    expression, index = OxmlElement("m:e"), OxmlElement("m:sub")
    expression.append(math_run(base))
    index.append(math_run(suffix))
    node.extend([expression, index])
    return node


def fraction(numerator, denominator):
    node = OxmlElement("m:f")
    num, den = OxmlElement("m:num"), OxmlElement("m:den")
    for item in numerator:
        num.append(deepcopy(item))
    for item in denominator:
        den.append(deepcopy(item))
    node.extend([num, den])
    return node


def equation(doc, name, number):
    paragraph = doc.add_paragraph()
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.first_line_indent = Pt(0)
    paragraph.paragraph_format.space_before = Pt(5)
    paragraph.paragraph_format.space_after = Pt(7)
    paragraph.paragraph_format.line_spacing = 1.2
    para = OxmlElement("m:oMathPara")
    props = OxmlElement("m:oMathParaPr")
    align = OxmlElement("m:jc")
    align.set(qn("m:val"), "center")
    props.append(align)
    para.append(props)
    math = OxmlElement("m:oMath")
    if name == "offset":
        parts = [math_run("θ = "), fraction([
            math_run("("), sub("t", "2"), math_run(" − "), sub("t", "1"),
            math_run(") + ("), sub("t", "3"), math_run(" − "), sub("t", "4"),
            math_run(")")], [math_run("2")])]
    elif name == "rtt":
        parts = [math_run("δ = ("), sub("t", "4"), math_run(" − "),
                 sub("t", "1"), math_run(") − ("), sub("t", "3"),
                 math_run(" − "), sub("t", "2"), math_run(")")]
    elif name == "envelope":
        parts = [sub("X", "i"), math_run("(T + τ) = ["),
                 sub("v", "i"), math_run(" − "), sub("E", "i"),
                 math_run(", "), sub("v", "i"), math_run(" + "),
                 sub("E", "i"), math_run("]")]
    elif name == "budget":
        parts = [sub("A", "i"), math_run(" = "), fraction([
            math_run("|"), sub("v", "i"), math_run(" − "), sub("h", "i"),
            math_run("| − "), sub("e", "i")], [sub("L", "i")])]
    else:
        raise ValueError(name)
    parts.append(math_run("      (" + number + ")"))
    for part in parts:
        math.append(part)
    para.append(math)
    paragraph._p.append(para)


def architecture():
    scale = 2
    image = Image.new("RGB", (1600 * scale, 560 * scale), "white")
    draw = ImageDraw.Draw(image)
    regular = "C:/Windows/Fonts/simsun.ttc"
    bold = "C:/Windows/Fonts/simhei.ttf"
    f = ImageFont.truetype(regular, 34 * scale)
    h = ImageFont.truetype(bold, 36 * scale)

    def box(bounds, lines, fill="white"):
        bounds = tuple(int(x * scale) for x in bounds)
        draw.rectangle(bounds, fill=fill, outline="#34465A", width=2 * scale)
        total = len(lines) * 42 * scale
        y = bounds[1] + (bounds[3] - bounds[1] - total) / 2
        for index, line in enumerate(lines):
            use = h if index == 0 else f
            width = draw.textlength(line, font=use)
            draw.text(((bounds[0]+bounds[2]-width)/2, y), line, font=use, fill="black")
            y += 42 * scale

    def arrow(start, end, both=False):
        x1, y1 = (int(x * scale) for x in start)
        x2, y2 = (int(x * scale) for x in end)
        draw.line((x1,y1,x2,y2), fill="#34465A", width=3 * scale)
        if y1 == y2:
            sign = 1 if x2 > x1 else -1
            draw.polygon([(x2,y2),(x2-sign*12*scale,y2-7*scale),
                          (x2-sign*12*scale,y2+7*scale)], fill="#34465A")
        else:
            sign = 1 if y2 > y1 else -1
            draw.polygon([(x2,y2),(x2-7*scale,y2-sign*12*scale),
                          (x2+7*scale,y2-sign*12*scale)], fill="#34465A")
        if both:
            draw.polygon([(x1,y1),(x1-7*scale,y1+12*scale),
                          (x1+7*scale,y1+12*scale)], fill="#34465A")

    box((40,265,340,490), ["多源输入", "AD7606 模拟量", "数字状态与事件", "RS485 传感器"])
    box((425,265,780,490), ["STM32H743 采集卡", "单调时基与采样区间", "冻结数据快照"])
    box((865,265,1195,490), ["联合规则", "时间预算与三态判定", "证据期限与补采"])
    box((1280,265,1560,490), ["板卡输出", "数字与模拟输出", "到期处理"])
    box((425,25,1195,170), ["Windows 上位机", "对时映射  规则编译下载  虚拟从站", "统一时间轴  动作核验  实验导出"], "#F0F3F6")
    arrow((340,375),(425,375))
    arrow((780,375),(865,375))
    arrow((1195,375),(1280,375))
    arrow((600,170),(600,265), both=True)
    arrow((1030,170),(1030,265), both=True)
    target = ROOT / "architecture.png"
    image.save(target)
    return target


def make_table(doc, block):
    caption = doc.add_paragraph(block["caption"], "Caption")
    caption.paragraph_format.keep_with_next = True
    table = doc.add_table(rows=1, cols=len(block["headers"]))
    table.alignment = WD_TABLE_ALIGNMENT.CENTER
    table.autofit = False
    borders = OxmlElement("w:tblBorders")
    for side in ("top", "left", "bottom", "right", "insideH", "insideV"):
        edge = OxmlElement("w:" + side)
        for key, value in (("val", "single"), ("sz", "5"), ("color", "D9D9D9")):
            edge.set(qn("w:" + key), value)
        borders.append(edge)
    table._tbl.tblPr.append(borders)
    margins = OxmlElement("w:tblCellMar")
    for side, value in (("top", "85"), ("bottom", "85"), ("left", "100"), ("right", "100")):
        tag = OxmlElement("w:" + side)
        tag.set(qn("w:w"), value)
        tag.set(qn("w:type"), "dxa")
        margins.append(tag)
    table._tbl.tblPr.append(margins)
    all_rows = [block["headers"], *block["rows"]]
    for row_index, values in enumerate(all_rows):
        row = table.rows[0] if row_index == 0 else table.add_row()
        cant_split = OxmlElement("w:cantSplit")
        row._tr.get_or_add_trPr().append(cant_split)
        if row_index == 0:
            repeat = OxmlElement("w:tblHeader")
            row._tr.get_or_add_trPr().append(repeat)
        for column_index, text in enumerate(values):
            cell = row.cells[column_index]
            cell.width = Cm(block["widths"][column_index])
            cell.vertical_alignment = WD_CELL_VERTICAL_ALIGNMENT.CENTER
            if row_index == 0:
                shade = OxmlElement("w:shd")
                shade.set(qn("w:fill"), "EDF0F3")
                cell._tc.get_or_add_tcPr().append(shade)
            paragraph = cell.paragraphs[0]
            paragraph.paragraph_format.first_line_indent = Pt(0)
            paragraph.paragraph_format.line_spacing = 1.1
            paragraph.paragraph_format.space_after = Pt(0)
            paragraph.paragraph_format.space_before = Pt(0)
            paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER if row_index == 0 or column_index == 0 else WD_ALIGN_PARAGRAPH.LEFT
            paragraph.paragraph_format.keep_with_next = row_index < len(all_rows) - 1
            run = paragraph.add_run(text)
            run_font(run, 10.5, row_index == 0)
    for grid, width in zip(table._tbl.tblGrid.gridCol_lst, block["widths"]):
        grid.set(qn("w:w"), str(int(Cm(width).twips)))
    spacer = doc.add_paragraph()
    spacer.paragraph_format.space_after = Pt(0)
    spacer.paragraph_format.space_before = Pt(0)
    spacer.paragraph_format.line_spacing = 1
    spacer.paragraph_format.first_line_indent = Pt(0)
    spacer.add_run().font.size = Pt(3)


def main():
    doc = Document()
    sec = doc.sections[0]
    sec.page_width, sec.page_height = Cm(21), Cm(29.7)
    sec.top_margin, sec.bottom_margin = Cm(2.2), Cm(2.2)
    sec.left_margin, sec.right_margin = Cm(2.6), Cm(2.6)
    sec.header_distance, sec.footer_distance = Cm(0.9), Cm(1.0)
    sec.different_first_page_header_footer = True

    normal = doc.styles["Normal"]
    font_style(normal, 12)
    normal.paragraph_format.line_spacing = 1.4
    normal.paragraph_format.space_after = Pt(5)
    normal.paragraph_format.first_line_indent = Pt(24)
    normal.paragraph_format.widow_control = True
    for name, size, chinese in (("Title", 22, "黑体"), ("Subtitle", 16, "宋体"),
                               ("Heading 1", 15, "黑体"), ("Heading 2", 12.5, "黑体")):
        style = doc.styles[name]
        font_style(style, size, chinese, bold=False)
        style.paragraph_format.first_line_indent = Pt(0)
        style.paragraph_format.space_before = Pt(12 if name == "Heading 1" else 8)
        style.paragraph_format.space_after = Pt(6)
        style.paragraph_format.keep_with_next = name.startswith("Heading")
        style.paragraph_format.line_spacing = 1.2
    caption = doc.styles["Caption"]
    font_style(caption, 10.5)
    caption.paragraph_format.first_line_indent = Pt(0)
    caption.paragraph_format.space_before = Pt(6)
    caption.paragraph_format.space_after = Pt(5)
    caption.paragraph_format.line_spacing = 1.1
    caption.paragraph_format.alignment = WD_ALIGN_PARAGRAPH.CENTER

    p = doc.add_paragraph(DATA["subtitle"], "Subtitle")
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.space_before = Pt(54)
    p.paragraph_format.space_after = Pt(30)
    p = doc.add_paragraph(DATA["title"], "Title")
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.space_after = Pt(54)
    fields = [("学院", "________________________"), ("专业", "测控技术与仪器"),
              ("学生姓名", "________________________"), ("学号", "________________________"),
              ("指导教师", "________________________")]
    for label, value in fields:
        p = doc.add_paragraph()
        p.paragraph_format.first_line_indent = Cm(2)
        p.paragraph_format.space_after = Pt(18)
        run_font(p.add_run(f"{label}：{value}"), 14)
    p = doc.add_paragraph(DATA["date"])
    p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    p.paragraph_format.first_line_indent = Pt(0)
    p.paragraph_format.space_before = Pt(34)
    run_font(p.runs[0], 12)
    sec = doc.add_section(WD_SECTION_START.NEW_PAGE)
    sec.different_first_page_header_footer = False
    sec.footer.is_linked_to_previous = False
    page_number = OxmlElement("w:pgNumType")
    page_number.set(qn("w:start"), "1")
    sec._sectPr.append(page_number)

    for block in DATA["blocks"]:
        kind = block["type"]
        if kind == "h1":
            p = doc.add_paragraph(block["text"], "Heading 1")
            if block["text"] == "九 参考文献":
                p.paragraph_format.page_break_before = True
        elif kind == "h2":
            doc.add_paragraph(block["text"], "Heading 2")
        elif kind in ("p", "ref"):
            p = doc.add_paragraph(block["text"])
            if kind == "ref":
                p.paragraph_format.first_line_indent = Pt(-18)
                p.paragraph_format.left_indent = Pt(18)
                p.paragraph_format.line_spacing = 1.15
                p.paragraph_format.space_after = Pt(7)
                p.alignment = WD_ALIGN_PARAGRAPH.LEFT
                for run in p.runs:
                    run_font(run, 10.5)
            else:
                p.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
                if block["text"].startswith("系统以 STM32H743"):
                    p.paragraph_format.keep_together = True
                    p.paragraph_format.keep_with_next = True
        elif kind == "table":
            make_table(doc, block)
        elif kind == "equation":
            equation(doc, block["name"], block["number"])
        elif kind == "figure":
            p = doc.add_paragraph()
            p.alignment = WD_ALIGN_PARAGRAPH.CENTER
            p.paragraph_format.first_line_indent = Pt(0)
            p.paragraph_format.keep_with_next = True
            picture = p.add_run().add_picture(str(architecture()), width=Cm(15.8))
            picture._inline.docPr.set("descr", "板载与RS485输入进入采集卡时间快照，再进入规则和输出；上位机负责对时、配置、回放和实验")
            doc.add_paragraph(block["caption"], "Caption")
        else:
            raise ValueError(kind)

    footer = sec.footer.paragraphs[0]
    footer.alignment = WD_ALIGN_PARAGRAPH.CENTER
    footer.paragraph_format.first_line_indent = Pt(0)
    field = OxmlElement("w:fldSimple")
    field.set(qn("w:instr"), "PAGE")
    footer._p.append(field)
    doc.core_properties.title = DATA["title"]
    doc.core_properties.subject = "本科毕业设计开题报告"
    doc.core_properties.author = ""
    doc.core_properties.keywords = "多源采集 时间统一 规则时间契约 联合控制 上位机"
    settings = doc.settings.element
    update = OxmlElement("w:updateFields")
    update.set(qn("w:val"), "true")
    settings.append(update)
    # Suppress the template's title border and any inherited paragraph rules.
    for tree in (doc.styles.element, doc.element):
        for border in list(tree.xpath(".//w:pBdr")):
            border.getparent().remove(border)
    doc.save(OUTPUT)
    body = "\n".join(block.get("text", "") for block in DATA["blocks"])
    print(json.dumps({"output": str(OUTPUT), "textCharacters": len(body),
                      "paragraphs": len(doc.paragraphs), "tables": len(doc.tables)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
