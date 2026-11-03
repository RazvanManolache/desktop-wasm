"""Complete upstream's required distribution branding in generated build input.

Never changes the pinned source. No feedback email or donation destination is
invented. The project URL identifies the original OpenPDN project.
"""
from pathlib import Path
import argparse
import re

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
args = parser.parse_args()
try:
    text = args.source.read_text(encoding='utf-8-sig')
except UnicodeDecodeError:
    text = args.source.read_text(encoding='cp1252')
values = {'FeedbackEmail': '', 'CrashlogEmail': '',
          'WebsiteUrl': 'https://github.com/rivy/OpenPDN',
          'DonateUrlSetup': '', 'SingleInstanceMonikerName': 'DesktopWasmOpenPDN'}
for name, value in values.items():
    pattern = rf'(public const string {name}\s*=\s*)""\s*<--[^\r\n]*;'
    text, count = re.subn(pattern, lambda m: m[1] + '"' + value + '";', text)
    if count != 1:
        raise ValueError(f'Expected exactly one upstream branding placeholder for {name}')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(text, encoding='utf-8-sig')
