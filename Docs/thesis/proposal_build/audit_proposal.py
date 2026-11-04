import json
import re
from pathlib import Path
from zipfile import ZipFile
from docx import Document

root = Path(__file__).resolve().parent
content = json.loads((root / 'content.json').read_text(encoding='utf-8'))
file = root.parent / '数据采集时间统一与联合控制_开题报告.docx'
doc = Document(file)
xml = ZipFile(file).read('word/document.xml').decode()
citations = set()
for block in content['blocks']:
    if block['type'] == 'p':
        for group in re.findall(r'\[([0-9-]+)\]', block['text']):
            ends = list(map(int, group.split('-')))
            citations.update(range(ends[0], ends[-1] + 1))
assert citations == set(range(1, 11)), citations
assert xml.count('<m:oMathPara>') == 4
assert len(doc.tables) == 4
assert content['title'] in [paragraph.text for paragraph in doc.paragraphs]
assert all(len(row['widths']) == len(row['headers']) for row in content['blocks'] if row['type'] == 'table')
print(json.dumps({'referenceIds': sorted(citations), 'nativeEquations': 4,
                  'tables': 4, 'headingCount': sum(paragraph.style.name.startswith('Heading') for paragraph in doc.paragraphs),
                  'bytes': file.stat().st_size}))
