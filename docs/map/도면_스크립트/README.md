# 도면 생성 스크립트 (맵설계 v1.2)

`docs/map/`의 도면 이미지(PNG · SVG)를 만드는 Matplotlib 스크립트다. 원본 데이터는 같은 폴더 위의 **`마르코_수영장맵_3D.html` 격자(RAW1 · RAW2)**이며, 이 격자는 `docs/맵설계.md` v1.2 표와 칸 단위로 대조를 마쳤다(2026-10-17, 불일치 0칸).

## 실행

```
pip install matplotlib
cd docs/map/도면_스크립트
python plan.py     # 1층 · 2층 연결상세 PNG
python graph.py    # 구역연결도 PNG · SVG
python iso.py      # 2.5D 결합도 · 분해도 PNG · SVG
```

결과는 한 단계 위 `docs/map/`에 `맵설계_v1.2_*.png / .svg`로 저장된다.

## 파일

| 파일 | 역할 |
|---|---|
| `mapdata.py` | 3D 뷰어 HTML에서 격자를 읽고, 뷰어와 같은 방식으로 구역(flood fill) · 문(폭 · 등급)을 계산한다 |
| `common.py` | 색 · 글꼴 · 버전 문자열(`VER`) · 라벨 문구 공통 함수 |
| `plan.py` | 평면 연결상세도. 구역 라벨 위치 · 문 라벨 위치는 `LABEL` · `DOFF`에서 손으로 정한다 |
| `graph.py` | 구역 연결도. 노드 위치(`POS`) · 루프 설명 문구는 손으로 정한다 |
| `iso.py` | 2.5D 아이소메트릭(남동쪽 시점, 벽 1.8m 컷). 라벨 위치는 `LAB1` · `LAB2` · `LAB_COMB1` |

## 맵이 바뀌었을 때

1. 3D 뷰어의 `RAW1` · `RAW2` 격자와 `SEEDS`(구역 이름 씨앗)를 먼저 고친다. 구역 이름이 새로 생기면 `mapdata.py`의 `SEEDS` · `CAT`에도 넣는다.
2. 버전이 바뀌면 `common.py`의 `VER`를 바꾼다(파일 이름 · 제목에 쓰임).
3. 세 스크립트를 다시 돌리고, 라벨이 겹치는 곳만 위치 표를 손본다.
4. `graph.py`의 루프 설명 문구는 자동으로 바뀌지 않으므로 직접 고친다.

## 주의

- 도면의 **면적은 칸 수(벽 칸 제외)**라 `docs/맵설계.md` 표의 벽 중심선 면적과 다르다. 정본 수치는 맵설계 표를 본다.
- 한글 글꼴은 Noto Sans CJK KR → Noto Sans KR → 맑은 고딕 → AppleGothic 순으로 있는 것을 쓴다. 글꼴이 다르면 라벨 폭이 조금 달라질 수 있다.
