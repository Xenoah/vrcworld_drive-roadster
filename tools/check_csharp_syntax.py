"""Optional tree-sitter syntax check. Does not resolve SDK types or compile Udon."""
from pathlib import Path
import sys
root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'tools/python-deps'))
from tree_sitter import Language, Parser
import tree_sitter_c_sharp
parser=Parser(Language(tree_sitter_c_sharp.language()))
failed=[]
for p in sorted((root/'Assets').rglob('*.cs')):
    tree=parser.parse(p.read_bytes())
    if tree.root_node.has_error: failed.append(str(p.relative_to(root)))
print({'parser':'tree-sitter-c-sharp', 'files':len(list((root/'Assets').rglob('*.cs'))), 'syntax_errors':failed, 'sdk_compile':'NOT RUN'})
raise SystemExit(bool(failed))
