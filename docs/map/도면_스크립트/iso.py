"""2.5D 결합도 / 분해도 (v1.2) — 남동쪽에서 내려다본 아이소메트릭, 벽 1.8m 컷"""
import sys
from common import *
from matplotlib.collections import PolyCollection, LineCollection
from matplotlib.patches import Patch
from matplotlib.lines import Line2D
from mapdata import load

import os
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
DATA = load()
C, S, KZ = 0.866, 0.5, 0.95
F2Z = 4.5
WALLH = 1.8
DOORC = '#f39c2a'
SIDE = {'wall': ('#2b2b2b', '#bab2a8', '#a59d93'), 'furn': ('#e3d49d', '#cbbd85', '#b8aa74'),
        'mach': ('#e3d49d', '#cbbd85', '#b8aa74'), 'lock': ('#cfc7bd', '#b3aba1', '#9f978d'),
        'duct': ('#8c8c8c', '#777', '#6a6a6a'), 'rail': ('#8a8a8a', '#777', '#6a6a6a'),
        'stair': ('#b3a3ea', '#9a8ad8', '#8a7ac8'), 'slab': (None, '#c9c2b8', '#b7b0a6')}

def P(x, y, z, ox=0.0, oy=0.0):
    return ((x + y) * C + ox, (y - x) * S + z * KZ + oy)

def quad(pts, ox, oy):
    return [P(*p, ox, oy) for p in pts]

def build(f, zbase, ox=0.0, oy=0.0):
    """returns (floor_polys, floor_cols, block_polys, block_cols, extra_lines)"""
    d = DATA[f]; g = d['grid']; zid = d['zid']; Z = d['zones']
    wet = {c for c, ch in g.items() if ch == '~'}
    for (x, y), ch in g.items():
        if ch in OBJC and sum(g.get((x + a, y + b)) == '~' for a, b in ((1, 0), (-1, 0), (0, 1), (0, -1))) >= 2:
            wet.add((x, y))
    fp, fc = [], []
    blocks = {}
    for (x, y), ch in g.items():
        zone = Z[zid[(x, y)]] if (x, y) in zid else None
        if ch == ',':
            continue
        if ch == '#':
            blocks[(x, y)] = (WALLH, 'wall'); continue
        if ch == ':':
            blocks[(x, y)] = (1.0, 'rail'); continue
        if ch == '%':
            blocks[(x, y)] = (0.5, 'duct'); continue
        if ch == '+':
            col = DOORC
        elif (x, y) in wet:
            col = WATER
        elif zone is not None:
            col = ZFILL[zone['cat']]
        else:
            col = STAIR if ch == '/' else '#eeeeee'
        fp.append(quad([(x, y, zbase), (x + 1, y, zbase), (x + 1, y + 1, zbase), (x, y + 1, zbase)], ox, oy)); fc.append(col)
        if ch == 'o':
            if zone and zone['name'] == '관람석':
                blocks[(x, y)] = (0.35 + (32 - y) * 0.32, 'furn')
            elif zone and zone['cat'] == 'plant':
                blocks[(x, y)] = (1.3, 'mach')
            else:
                blocks[(x, y)] = (0.6, 'furn')
        elif ch == '&':
            blocks[(x, y)] = (1.4, 'lock')
        elif ch == '/':
            blocks[(x, y)] = (0.25, 'stair')
    # pool edge lines
    lines = []
    for (x, y), ch in g.items():
        if (x, y) not in wet:
            continue
        for (dx, dy, a, b) in ((0, -1, (x, y), (x + 1, y)), (0, 1, (x, y + 1), (x + 1, y + 1)),
                               (-1, 0, (x, y), (x, y + 1)), (1, 0, (x + 1, y), (x + 1, y + 1))):
            if (x + dx, y + dy) not in wet:
                lines.append([P(a[0], a[1], zbase, ox, oy), P(b[0], b[1], zbase, ox, oy)])
    # 2F slab edges (thin side faces under the floor)
    slab = []
    if f == 1:
        floorcells = {c for c, ch in g.items() if ch not in (',',)}
        for (x, y) in floorcells:
            t = 0.35
            if (x, y - 1) not in floorcells:
                slab.append(((x - y), quad([(x, y, zbase - t), (x + 1, y, zbase - t), (x + 1, y, zbase), (x, y, zbase)], ox, oy), SIDE['slab'][1]))
            if (x + 1, y) not in floorcells:
                slab.append(((x - y), quad([(x + 1, y, zbase - t), (x + 1, y + 1, zbase - t), (x + 1, y + 1, zbase), (x + 1, y, zbase)], ox, oy), SIDE['slab'][2]))
    bp, bc = [], []
    order = sorted(blocks.items(), key=lambda kv: (kv[0][0] - kv[0][1], kv[0][0]))
    for (x, y), (h, kind) in order:
        top, sc, ec = SIDE[kind]
        z0, z1 = zbase, zbase + h
        nb = blocks.get((x, y - 1))
        if not nb or nb[0] < h:
            bp.append(quad([(x, y, z0), (x + 1, y, z0), (x + 1, y, z1), (x, y, z1)], ox, oy)); bc.append(sc)
        nb = blocks.get((x + 1, y))
        if not nb or nb[0] < h:
            bp.append(quad([(x + 1, y, z0), (x + 1, y + 1, z0), (x + 1, y + 1, z1), (x + 1, y, z1)], ox, oy)); bc.append(ec)
        bp.append(quad([(x, y, z1), (x + 1, y, z1), (x + 1, y + 1, z1), (x, y + 1, z1)], ox, oy)); bc.append(top)
    sp = [q for _, q, _ in sorted(slab, key=lambda t: t[0])]; sc_ = [c for _, _, c in sorted(slab, key=lambda t: t[0])]
    objs = [(x, y, ch) for (x, y), ch in g.items() if ch in OBJC]
    return dict(fp=fp, fc=fc, bp=bp, bc=bc, lines=lines, sp=sp, sc=sc_, objs=objs, zbase=zbase, ox=ox, oy=oy)

def paint(ax, L, pinscale=1.0):
    ax.add_collection(PolyCollection(L['sp'], facecolors=L['sc'], edgecolors=L['sc'], linewidths=0.3))
    ax.add_collection(PolyCollection(L['fp'], facecolors=L['fc'], edgecolors=L['fc'], linewidths=0.6))
    ax.add_collection(LineCollection(L['lines'], colors='#3d8fc9', linewidths=2.2 * pinscale))
    ax.add_collection(PolyCollection(L['bp'], facecolors=L['bc'], edgecolors=L['bc'], linewidths=0.3))

def pins(ax, L, s=1.0):
    for x, y, ch in sorted(L['objs'], key=lambda t: t[0] - t[1]):
        x0, y0 = P(x + .5, y + .5, L['zbase'], L['ox'], L['oy'])
        x1, y1 = P(x + .5, y + .5, L['zbase'] + 2.6, L['ox'], L['oy'])
        ax.plot([x0, x1], [y0, y1], color=OBJC[ch], lw=2.0 * s, zorder=20)
        ax.plot([x1], [y1], 'o', ms=17 * s, mfc=OBJC[ch], mec='white', mew=1.4, zorder=21)
        ax.text(x1, y1, ch, ha='center', va='center', color='white', fontsize=10 * s, weight='bold', zorder=22)

def labels(ax, L, items, fs):
    for t, x, y in items:
        px, py = P(x, y, L['zbase'] + 2.2, L['ox'], L['oy'])
        ax.text(px, py, t, ha='center', va='center', fontsize=fs, zorder=30,
                bbox=dict(boxstyle='round,pad=0.3', fc='white', ec='#888', lw=1.0, alpha=0.96))

LAB1 = [("교습풀", 8.0, 47.0), ("메인 풀 홀 · 경영풀 25×21", 28.5, 47.0), ("구조 · 응급실", 4.5, 20.0),
        ("샤워 L(남)", 15.5, 28.0), ("탈의실 L(남)", 17.0, 17.0), ("공조 덕트(출입 불가)", 27.5, 21.0),
        ("샤워 R(여)", 38.0, 28.0), ("탈의실 R(여)", 37.0, 17.0), ("로비 (도망자 스폰 R)", 14.0, 4.0),
        ("직원 통로 (폭 5m)", 50.8, 40.0), ("기계실 · 막다른 방", 59.0, 48.0), ("하역장 (출구)", 57.0, 58.5),
        ("약품 저장실", 52.5, 22.5), ("직원 계단", 51.0, 13.0), ("관리 사무실", 52.5, 4.0), ("직원 구역", 60.5, 13.0)]
LAB2 = [("물탱크실 · 막다른 방", 4.5, 20.0), ("2층 로비", 28.0, 21.0), ("관람석 (계단식)", 29.0, 29.5),
        ("관리 복도 (술래 스폰 S)", 52.0, 18.0)]
LAB_COMB1 = [("교습풀", 8.0, 47.0), ("메인 풀 홀 · 경영풀 25×21", 28.5, 47.0), ("로비 (도망자 스폰 R)", 14.0, 4.0),
             ("직원 통로 (폭 5m)", 50.8, 40.0), ("기계실 · 막다른 방", 59.0, 48.0), ("하역장 (출구)", 57.0, 58.5),
             ("약품 저장실", 52.5, 22.5), ("직원 계단", 51.0, 12.0), ("관리 사무실", 52.5, 4.0), ("직원 구역", 60.5, 13.0)]

def legend_handles():
    def sq(c, l, **k): return Patch(fc=c, ec=k.get('ec', 'none'), label=l)
    def dot(c, l): return Line2D([], [], marker='o', ls='', ms=13, mfc=c, mec='white', label=l)
    return [sq('#2b2b2b', '벽 (내부가 보이게 1.8m로 잘라 그림)'), sq(ZFILL['hall'], '홀 바닥', ec='#bbb'),
            sq(ZFILL['guest'], '이용객 구역 바닥', ec='#bbb'), sq(ZFILL['shower'], '샤워실 바닥', ec='#bbb'),
            sq(ZFILL['aid'], '응급 · 구조 바닥', ec='#bbb'), sq(ZFILL['staff'], '직원 바닥', ec='#bbb'),
            sq(ZFILL['plant'], '설비실 바닥', ec='#bbb'), sq(ZFILL['spect'], '관람객 구역(2층) 바닥', ec='#bbb'),
            sq('#8c8c8c', '설비 공간(출입 불가)'), sq(WATER, '물 (풀)'), sq(DOORC, '문'),
            sq('#cfc7bd', '락커 · 칸막이'), sq('#e3d49d', '가구 · 설비 · 좌석'), sq(STAIR, '계단'),
            dot(OBJC['A'], 'A~E 밸브'), dot(OBJC['X'], 'X 출구'), dot(OBJC['R'], 'R 도망자 스폰'),
            dot(OBJC['S'], 'S 술래 스폰'), dot(OBJC['b'], 'b 붕대 후보'), dot(OBJC['c'], 'c 찰칵이'), dot(OBJC['@'], '@ 배수구')]

def north(ax, x, y, L=4.0):
    x0, y0 = x, y
    x1, y1 = x + L * C, y + L * S
    ax.annotate('', xy=(x1, y1), xytext=(x0, y0), arrowprops=dict(arrowstyle='-|>', lw=2.6, color='#222', mutation_scale=30))
    ax.text(x1 + 0.8, y1 + 1.0, '북 N', fontsize=15, weight='bold')

def combined(png, svg):
    fig = plt.figure(figsize=(25.33, 19.94), dpi=100)
    ax = fig.add_axes([0.02, 0.17, 0.96, 0.74]); ax.set_aspect('equal'); ax.axis('off')
    L1 = build(0, 0.0); L2 = build(1, F2Z)
    paint(ax, L1); pins(ax, L1); paint(ax, L2); pins(ax, L2)
    labels(ax, L1, LAB_COMB1, 16); labels(ax, L2, LAB2[:3] + [("관리 복도 (술래 스폰 S)", 49.5, 16.5)], 16)
    ax.set_xlim(-3, 113); ax.set_ylim(-36, 38)
    north(ax, -1.0, 27.0)
    fig.text(0.02, 0.975, f'맵설계 {VER} — 1층 + 2층 결합도 (2층 바닥 높이 4.5m)', fontsize=26, weight='bold', va='top')
    fig.text(0.02, 0.948, '2층을 실제 높이에 올려 그린 것. 관람석이 탈의 · 샤워 구역 위에 놓이고 북쪽의 풀을 내려다보는 관계를 보여준다. '
             '2층 아래에 가려진 1층은 분해도에서 확인.', fontsize=14, color='#444', va='top')
    fig.legend(handles=legend_handles(), loc='lower left', ncol=4, fontsize=14, frameon=False,
               bbox_to_anchor=(0.02, 0.012), handlelength=1.4, columnspacing=4.0, labelspacing=0.9)
    fig.savefig(png, dpi=100); fig.savefig(svg); plt.close(fig)

def exploded(png, svg):
    fig = plt.figure(figsize=(49.39, 18.69), dpi=100)
    ax = fig.add_axes([0.005, 0.13, 0.99, 0.79]); ax.set_aspect('equal'); ax.axis('off')
    GAP = 118
    L1 = build(0, 0.0); L2 = build(1, 0.0, ox=GAP)
    paint(ax, L1); pins(ax, L1); paint(ax, L2); pins(ax, L2)
    labels(ax, L1, LAB1, 15); labels(ax, L2, LAB2 + [("홀 상부 — 아래층이 보이는 빈 공간", 26.0, 47.0)], 15)
    ax.set_xlim(-4, GAP + 112); ax.set_ylim(-36, 37)
    ax.text(-2, 33, '1층', fontsize=24, weight='bold'); ax.text(GAP - 6, 33, '2층', fontsize=24, weight='bold')
    north(ax, 2.0, 23.0, 3.5)
    fig.text(0.005, 0.975, f'마르코! 심야 실내수영장 — 맵설계 {VER} (2.5D 분해도)', fontsize=26, weight='bold', va='top')
    fig.text(0.005, 0.952, 'docs/맵설계.md v1.2의 도면(3D 뷰어 격자와 칸 단위 대조 완료)을 1칸 = 1m로 입체화. 남동쪽에서 내려다본 시점. '
             '벽은 내부가 보이게 1.8m로 잘라 그림. v1.2: 구조 · 응급실 · 직원 구역 통합, 직원 통로 5m, 기계실 문 북쪽 이동, 하역장 문 4.0m 양개.',
             fontsize=14, color='#444', va='top')
    fig.legend(handles=legend_handles(), loc='lower left', ncol=8, fontsize=14, frameon=False,
               bbox_to_anchor=(0.005, 0.01), handlelength=1.4, columnspacing=3.0, labelspacing=0.9)
    fig.savefig(png, dpi=100); fig.savefig(svg); plt.close(fig)

if __name__ == '__main__':
    which = sys.argv[2] if len(sys.argv) > 2 else 'both'
    if which in ('both', 'comb'):
        combined(f'{OUT}/맵설계_{VER}_2.5D_결합도.png', f'{OUT}/맵설계_{VER}_2.5D_결합도.svg')
    if which in ('both', 'expl'):
        exploded(f'{OUT}/맵설계_{VER}_2.5D_분해도.png', f'{OUT}/맵설계_{VER}_2.5D_분해도.svg')
