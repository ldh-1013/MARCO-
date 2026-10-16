import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib import font_manager

_have = {f.name for f in font_manager.fontManager.ttflist}
FONT = next((f for f in ('Noto Sans CJK KR', 'Noto Sans KR', 'Malgun Gothic', 'AppleGothic') if f in _have), 'sans-serif')
plt.rcParams['font.family'] = FONT
plt.rcParams['axes.unicode_minus'] = False
plt.rcParams['svg.hashsalt'] = 'marco-v12'

VER = 'v1.2'

ZFILL = {'hall': '#d8f0f2', 'guest': '#e0f0d8', 'shower': '#c8ebc0', 'aid': '#f7d9e3',
         'staff': '#f3e3c8', 'plant': '#ecd9bf', 'spect': '#e6defa'}
OBJC = {'A': '#d62728', 'B': '#d62728', 'C': '#d62728', 'D': '#d62728', 'E': '#d62728',
        'X': '#1f9e55', 'R': '#17a2b8', 'S': '#e8590c', 'b': '#e64980', 'c': '#e0a800', '@': '#0b3d91'}
WALL = '#2b2b2b'; FURN = '#d9c98f'; LOCK = '#a9a39b'; STAIR = '#b3a3ea'; WATER = '#7dc3ea'
DUCT = '#bdbdbd'; RAIL = '#7a7a7a'; VOID = '#f1f1f5'

def objsummary(objs):
    parts = []
    for v in 'ABCDE':
        if objs.get(v):
            parts.append(f'밸브 {v}')
    if objs.get('X'): parts.append('출구')
    if objs.get('R'): parts.append('도망자 스폰')
    if objs.get('S'): parts.append('술래 스폰')
    if objs.get('@'): parts.append(f"배수구 {objs['@']}")
    if objs.get('b'): parts.append(f"붕대 {objs['b']}")
    if objs.get('c'): parts.append(f"찰칵이 {objs['c']}")
    return ' · '.join(parts)

def doorlabel(d):
    w = d['width']
    return f'{w:.1f}m' if d['exact'] else f'약 {int(w)}m'
