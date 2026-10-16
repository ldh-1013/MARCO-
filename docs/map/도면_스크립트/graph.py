"""구역 연결도 (v1.2) — 노드 = 구역, 선 = 문(폭 등급), 점선 = 계단"""
import sys
from common import *
from matplotlib.patches import FancyBboxPatch, Rectangle
from matplotlib.lines import Line2D
from matplotlib.path import Path
from matplotlib.patches import PathPatch
from mapdata import load, grade

import os
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
DATA = load()
# figure pixel coords (2640 x 1650 canvas, y down) — layout follows real placement roughly
POS = {
    "하역장 출구(외부)": (1142, 228), "하역장": (1108, 357), "메인 풀 홀": (587, 548),
    "구조 · 응급실": (163, 960), "샤워실 L(남)": (412, 911), "샤워실 R(여)": (798, 911),
    "탈의실 L(남)": (422, 1089), "탈의실 R(여)": (782, 1089), "로비": (521, 1318), "정문(외부)": (554, 1498),
    "직원 통로": (977, 748), "기계실": (1222, 634), "약품 저장실": (1158, 845), "직원 계단실": (1076, 1010),
    "직원 구역": (1270, 1080), "관리 사무실": (977, 1221),
    "물탱크실": (1613, 862), "관람석": (1987, 815), "2층 로비": (1987, 1043), "관리 복도": (2377, 1171),
}
FLOOR = {n: (1 if n in ("물탱크실", "관람석", "2층 로비", "관리 복도") else 0) for n in POS}
STAIR = [("메인 풀 홀", "관람석", "계단 S3", -0.20), ("로비", "2층 로비", "계단 S1", -0.30),
         ("직원 계단실", "관리 복도", "계단 S2", 0.10)]

def lw_of(w): return 2.5 + 2.0 * min(w, 4)

def main(svg_path, png_path):
    W, H = 2640, 1650
    fig = plt.figure(figsize=(W / 100, H / 100), dpi=100)
    ax = fig.add_axes([0, 0, 1, 1]); ax.set_xlim(0, W); ax.set_ylim(H, 0); ax.axis('off')
    ax.add_patch(Rectangle((33, 82), 1335, 1497, fc='#fafafa', ec='#cccccc', lw=1.2, zorder=0))
    ax.add_patch(Rectangle((1368, 82), 1240, 1497, fc='#f6f4fd', ec='#cccccc', lw=1.2, zorder=0))
    ax.text(66, 108, '1층', fontsize=26, weight='bold', va='center', color='#444')
    ax.text(1400, 108, '2층', fontsize=26, weight='bold', va='center', color='#444')
    fig.text(0.007, 0.985, f'맵설계 {VER} — 구역 연결도 (어디에서 어디로 이어지는가)', fontsize=22, weight='bold', va='top')

    zinfo = {}
    for f in (0, 1):
        for z in DATA[f]['zones']:
            zinfo[z['name']] = z
    # group doors per zone pair
    edges = {}
    for f in (0, 1):
        for d in DATA[f]['doors']:
            if d['outside']:
                ext = '하역장 출구(외부)' if '하역장' in d['zones'] else '정문(외부)'
                key = (d['zones'][0], ext)
            else:
                key = tuple(d['zones'])
            edges.setdefault(key, []).append(d)

    for (a, b), ds in edges.items():
        (x1, y1), (x2, y2) = POS[a], POS[b]
        w = max(d['width'] for d in ds)
        col = grade(w)[1]
        ax.plot([x1, x2], [y1, y2], color=col, lw=lw_of(w) * 0.9, solid_capstyle='round', zorder=2, alpha=0.9)
        nm = grade(w)[0]
        if b.endswith('(외부)'):
            t = f"외부 출구 약 {int(w)}m"
        elif len(ds) > 1:
            t = '문 ' + ' + '.join(doorlabel(d) for d in ds)
        else:
            t = f"{doorlabel(ds[0])} {nm}"
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        ax.text(mx, my, t, ha='center', va='center', fontsize=12, color=col, weight='bold', zorder=4,
                bbox=dict(boxstyle='round,pad=0.2', fc='white', ec=col, lw=1.1))

    for a, b, lab, rad in STAIR:
        (x1, y1), (x2, y2) = POS[a], POS[b]
        mx, my = (x1 + x2) / 2, (y1 + y2) / 2
        dx, dy = x2 - x1, y2 - y1
        cx, cy = mx - dy * rad, my + dx * rad
        p = Path([(x1, y1), (cx, cy), (x2, y2)], [Path.MOVETO, Path.CURVE3, Path.CURVE3])
        ax.add_patch(PathPatch(p, fc='none', ec='#7048e8', lw=3.2, ls=(0, (5, 3)), zorder=1))
        t = 0.72 if lab == '계단 S1' else 0.5
        lx = (1 - t) ** 2 * x1 + 2 * t * (1 - t) * cx + t * t * x2
        ly = (1 - t) ** 2 * y1 + 2 * t * (1 - t) * cy + t * t * y2
        ax.text(lx, ly, lab, ha='center', va='center', fontsize=13, color='#5b3fd0', zorder=4,
                bbox=dict(boxstyle='round,pad=0.2', fc='white', ec='#7048e8', lw=1.1))

    for n, (x, y) in POS.items():
        if n.endswith('(외부)'):
            ax.text(x, y, n, ha='center', va='center', fontsize=15, color='white', weight='bold', zorder=6,
                    bbox=dict(boxstyle='round,pad=0.45', fc='#1f9e55', ec='#1f9e55'))
            continue
        z = zinfo[n]; s = objsummary(z['objs'])
        t = f"{n}\n{z['area']}m²" + (f"\n{s}" if s else '')
        fs = 15.5 if z['area'] >= 400 else 14
        ax.text(x, y, t, ha='center', va='center', fontsize=fs, weight='bold', zorder=6, linespacing=1.2,
                bbox=dict(boxstyle='round,pad=0.45', fc=ZFILL[z['cat']], ec='#555', lw=1.4))

    loops = ("주요 루프 (한 바퀴 돌 수 있는 길)\n"
             "① 이용객 루프: 로비 → 탈의실 L → 샤워실 L → 홀 → 샤워실 R → 탈의실 R → 로비\n"
             "② 서쪽 루프: 로비 → 구조 · 응급실 → 홀 → 샤워실 L → 탈의실 L → 로비\n"
             "③ 동쪽 루프: 홀 → 직원 통로 → 관리 사무실 → 로비 → 탈의실 R → 샤워실 R → 홀\n"
             "④ 직원 구역 루프: 직원 통로 → 약품 저장실 → 직원 구역 → 관리 사무실 → 직원 통로 (약 52m)\n"
             "⑤ 수직 루프: 1층 로비 → S1 → 2층 로비 → 관람석 → S3 → 홀 → 샤워 · 탈의 → 로비\n"
             "⑥ 하역장 루프: 홀 → 하역장 → 직원 통로 → 홀\n\n"
             "막다른 방: 기계실(밸브 A, 문 1개) · 물탱크실(밸브 C, 문 1개)\n"
             "홀 개방 출구: 샤워실 L · 샤워실 R · 하역장 (각 4.0m)\n"
             "v1.2: 직원 통로 전 구간 5m(개방 등급) · 직원 통로 ↔ 하역장 4.0m 양개 · 기계실 문 북쪽 이동")
    ax.text(1418, 205, loops, ha='left', va='top', fontsize=14, linespacing=1.55, zorder=5,
            bbox=dict(boxstyle='round,pad=0.8', fc='white', ec='#888', lw=1.2))

    H_ = [Line2D([], [], color='#2f9e44', lw=8, label='개방 문 (3.1m 이상 · 술래 옆을 스쳐 지나갈 수 있음)'),
          Line2D([], [], color='#f08c00', lw=8, label='경계 문 (2.4~3.1m)'),
          Line2D([], [], color='#d62728', lw=8, label='병목 문 (2.4m 미만 · 술래가 막으면 못 지나감)'),
          Line2D([], [], color='#7048e8', lw=3.2, ls=(0, (5, 3)), label='층간 계단')]
    ax.legend(handles=H_, loc='lower right', fontsize=14, frameon=True, bbox_to_anchor=(0.997, 0.032))
    fig.text(0.007, 0.008, "노드 위치는 실제 배치를 대략 따름. 선 굵기 = 문 폭. 홀 출입구는 보고서 값(소수점), '약 Nm'는 도면 칸 수 근사값. "
             "면적은 도면 칸 수 기준(맵설계 표의 벽 중심선 면적과 다름).", fontsize=12, color='#666')
    fig.savefig(png_path, dpi=100)
    fig.savefig(svg_path)
    plt.close(fig)

if __name__ == '__main__':
    main(f'{OUT}/맵설계_{VER}_구역연결도.svg', f'{OUT}/맵설계_{VER}_구역연결도.png')
