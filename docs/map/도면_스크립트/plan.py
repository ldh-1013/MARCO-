"""1층 / 2층 구역 · 연결 상세도 (v1.2)"""
import sys
from common import *
from matplotlib.patches import Rectangle, Circle, Patch
from matplotlib.lines import Line2D
from mapdata import load, grade

import os
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
DATA = load()

LABEL = [
    {"메인 풀 홀": (20.5, 59.6), "로비": (16.0, 4.8), "탈의실 L(남)": (17.0, 19.6), "탈의실 R(여)": (36.6, 19.6),
     "샤워실 L(남)": (15.5, 28.0), "샤워실 R(여)": (38.0, 28.0), "구조 · 응급실": (4.6, 19.5),
     "직원 통로": (50.9, 48.6), "기계실": (59.0, 45.0), "약품 저장실": (52.6, 24.1), "직원 계단실": (51.0, 12.0),
     "하역장": (57.4, 59.0), "관리 사무실": (52.5, 4.6), "직원 구역": (60.6, 12.4)},
    {"2층 로비": (28.5, 22.0), "관람석": (29.0, 29.8), "물탱크실": (4.6, 21.6), "관리 복도": (51.5, 18.4)},
]
STAIRS = [
    [("S1 ↕ 2층 로비", 27.5, 12.2), ("S2 ↕ 2층 관리 복도", 54.6, 17.6), ("S3 ↕ 2층 관람석", 12.4, 33.6)],
    [("S1 ↕ 1층 로비", 27.5, 12.5), ("S2 ↕ 1층 직원 계단실", 56.6, 12.5), ("S3 ↕ 1층 홀(데크)", 12.4, 31.4)],
]
# door-label offsets (dx, dy) by door center; default chosen from orientation
DOFF = {(56, 4): (0.6, 1.4), (46, 1.5): (1.9, 0.0), (48, 13.5): (1.9, 0), (48, 22.5): (-1.7, 0),
        (56, 22.5): (1.9, 0), (53, 42.5): (1.9, 0), (48, 35.5): (-1.9, 0), (46, 57.5): (1.9, 0),
        (46.5, 9): (0, 1.15), (49.5, 54): (0, 1.15)}

def draw(f, path):
    d = DATA[f]; g = d['grid']; zid = d['zid']; Z = d['zones']
    fig = plt.figure(figsize=(20.4, 19.8), dpi=100)
    ax = fig.add_axes([0.085, 0.115, 0.84, 0.835])
    ax.set_xlim(-1.5, 65.5); ax.set_ylim(-3.2, 64.4); ax.set_aspect('equal')
    ax.set_xticks(range(0, 65, 5)); ax.set_yticks(range(0, 62, 5))
    ax.tick_params(labelsize=10)
    ax.grid(color='#e6e6e6', lw=0.6, zorder=0)
    doorcell = {}
    for dr in d['doors']:
        for c in dr['cells']:
            doorcell[c] = dr
    for (x, y), ch in g.items():
        fc = None; hatch = None; ec = 'none'; lw = 0
        if ch == '#':
            fc = WALL
        elif ch == '+':
            fc = grade(doorcell[(x, y)]['width'])[1]; ec = 'white'; lw = 0.4
        elif ch == ',':
            fc = VOID; hatch = '..'; ec = '#c9c9d6'
        elif ch == ':':
            fc = RAIL
        elif ch == '%':
            fc = DUCT; hatch = 'xx'; ec = '#8a8a8a'
        else:
            z = Z[zid[(x, y)]] if (x, y) in zid else None
            base = ZFILL[z['cat']] if z else '#eeeeee'
            ax.add_patch(Rectangle((x, y), 1, 1, fc=base, ec='none', zorder=1))
            if ch == '~': fc = WATER
            elif ch == '/': fc = STAIR; ec = '#9a8ad8'; lw = 0.4
            elif ch == 'o': fc = FURN; ec = base; lw = 2.2
            elif ch == '&': fc = LOCK; ec = base; lw = 2.2
            if fc is None:
                continue
        ax.add_patch(Rectangle((x, y), 1, 1, fc=fc, ec=ec, lw=lw, hatch=hatch, zorder=2))
    # objects
    for (x, y), ch in g.items():
        if ch in OBJC:
            ax.add_patch(Circle((x + .5, y + .5), 0.78, fc=OBJC[ch], ec='white', lw=1.2, zorder=6))
            ax.text(x + .5, y + .5, ch, ha='center', va='center', color='white', fontsize=9, weight='bold', zorder=7)
    # door labels
    for dr in d['doors']:
        xs = [c[0] for c in dr['cells']]; ys = [c[1] for c in dr['cells']]
        cx = (min(xs) + max(xs) + 1) / 2; cy = (min(ys) + max(ys) + 1) / 2
        horiz = (max(xs) - min(xs)) >= (max(ys) - min(ys))
        col = grade(dr['width'])[1]
        txt = doorlabel(dr)
        if dr['outside']:
            txt = f"약 {int(dr['width'])}m 외부 출구"
            ty = cy + 1.5 if cy > 30 else cy - 1.6
            ax.text(cx, ty, txt, ha='center', va='center', fontsize=10, color=col, weight='bold', zorder=9,
                    bbox=dict(boxstyle='round,pad=0.15', fc='white', ec=col, lw=0.9))
            continue
        key = (min(xs) + (max(xs) - min(xs)) / 2, min(ys) + (max(ys) - min(ys)) / 2)
        dx, dy = DOFF.get(key, (0, 1.15) if horiz else (1.9, 0))
        ax.text(cx + dx, cy + dy, txt, ha='center', va='center', fontsize=10, color=col, weight='bold', zorder=9,
                bbox=dict(boxstyle='round,pad=0.15', fc='white', ec=col, lw=0.9))
    # zone labels
    for z in Z:
        if z['name'] not in LABEL[f]:
            continue
        lx, ly = LABEL[f][z['name']]
        s = objsummary(z['objs'])
        t = f"{z['name']}\n{z['area']}m²" + (f"\n{s}" if s else '')
        fs = 14 if z['area'] > 150 else 12
        ax.text(lx, ly, t, ha='center', va='center', fontsize=fs, weight='bold', zorder=10, linespacing=1.15,
                bbox=dict(boxstyle='round,pad=0.3', fc='white', ec='#777777', lw=1.0, alpha=0.95))
    for t, x, y in STAIRS[f]:
        ax.text(x, y, t, ha='center', va='center', fontsize=10.5, color='#5b3fd0', weight='bold', zorder=9,
                bbox=dict(boxstyle='round,pad=0.15', fc='white', ec='#5b3fd0', lw=0.9))
    title = f"맵설계 {VER} — 1층 구역 · 연결 상세도" if f == 0 else f"맵설계 {VER} — 2층 구역 · 연결 상세도  (점무늬 = 홀 위로 뚫린 공간)"
    fig.text(0.085, 0.988, title, fontsize=21, weight='bold', va='top')
    if f == 0:
        fig.text(0.085, 0.966, "v1.2 변경: 구조 · 응급실(응급처치실 + 구조원실), 직원 구역(방재실 + 휴게실 + 시설 관리실) 통합 · 직원 통로 전 구간 5m · 기계실 문 북쪽 이동(y 42~44) · 하역장 문 d19 4.0m 양개",
                 fontsize=11.5, color='#444', va='top')
    # legend
    H = [Patch(fc=ZFILL['hall'], ec='#999', label='홀'), Patch(fc=ZFILL['guest'], ec='#999', label='이용객 구역(로비 · 탈의)'),
         Patch(fc=ZFILL['shower'], ec='#999', label='샤워실'), Patch(fc=ZFILL['aid'], ec='#999', label='응급 · 구조'),
         Patch(fc=ZFILL['staff'], ec='#999', label='직원'), Patch(fc=ZFILL['plant'], ec='#999', label='설비실'),
         Patch(fc=ZFILL['spect'], ec='#999', label='관람객 구역(2층)'), Patch(fc=DUCT, ec='#8a8a8a', hatch='xx', label='설비 공간(출입 불가)'),
         Patch(fc=WATER, label='물'), Patch(fc=FURN, label='o 가구 · 설비 · 기둥(이동만 막음)'),
         Patch(fc=LOCK, label='& 락커(소리 · 태그 차단)'), Patch(fc=STAIR, label='계단'),
         Patch(fc='#2f9e44', label='문: 개방(3.1m 이상 · 술래 옆을 스쳐 지나감)'), Patch(fc='#f08c00', label='문: 경계(2.4~3.1m)'),
         Patch(fc='#d62728', label='문: 병목(2.4m 미만)')]
    def dot(c, l): return Line2D([], [], marker='o', ls='', ms=11, mfc=c, mec='white', label=l)
    H += [dot(OBJC['A'], 'A~E 밸브'), dot(OBJC['X'], 'X 출구'), dot(OBJC['R'], 'R 도망자 스폰'), dot(OBJC['S'], 'S 술래 스폰'),
          dot(OBJC['b'], 'b 붕대 후보'), dot(OBJC['c'], 'c 찰칵이'), dot(OBJC['@'], '@ 배수구(2곳 중 1곳 활성)')]
    fig.legend(handles=H, loc='lower center', ncol=5, fontsize=12, frameon=False, bbox_to_anchor=(0.5, 0.012),
               handlelength=2.2, columnspacing=1.6)
    fig.text(0.01, 0.004, "1칸 = 1m. 면적은 도면 칸 수(벽 칸 제외)로, 맵설계 표의 벽 중심선 면적과 다르다. 홀 출입구 폭은 보고서 값(소수점 표기), "
             "나머지 '약 Nm'는 칸 수 근사값. 정본은 docs/맵설계.md v1.2.", fontsize=10, color='#666')
    fig.savefig(path, dpi=100)
    plt.close(fig)

if __name__ == '__main__':
    draw(0, f'{OUT}/맵설계_{VER}_1층_연결상세.png')
    draw(1, f'{OUT}/맵설계_{VER}_2층_연결상세.png')
