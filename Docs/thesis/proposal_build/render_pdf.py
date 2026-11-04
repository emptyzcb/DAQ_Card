"""Rasterize the native Word export when LibreOffice is unavailable on Windows."""
import json
from pathlib import Path
from pdf2image import convert_from_path
from pypdf import PdfReader

root = Path(__file__).resolve().parent
pdf = root / "qa" / "proposal.pdf"
out = pdf.parent
document = PdfReader(str(pdf))
images = convert_from_path(str(pdf), dpi=144,
    poppler_path='C:/Users/86177/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/poppler/Library/bin',
    fmt='png', thread_count=2)
summary = []
for index, page in enumerate(document.pages):
    images[index].save(out / f"page-{index + 1}.png")
    text = page.extract_text() or ''
    summary.append({
        "page": index + 1,
        "characters": len(text),
        "start": text[:130].replace("\n", " "),
        "end": text[-130:].replace("\n", " "),
    })
(out / "pages.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"pages": len(document.pages), "summary": summary}, ensure_ascii=True))
