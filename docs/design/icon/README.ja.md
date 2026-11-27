# Moyai アイコン

ロープの結び目を図案化した Moyai のアイコンです。三つの交差と左右のロープ端で、結び目を表しています。特定の結び方の手順図ではありません。背景は透明で、黒い太線で描いています。

## 種類

| 用途 | 見た目 | ファイル |
| --- | --- | --- |
| アプリアイコン（48px 以上） | 太い黒縁・白塗りの結び目 | `out/moyai.svg`、`out/moyai_*.png` |
| タスクトレイ・小サイズ（32px 以下） | アプリと同じ黒縁・白塗りの結び目 | `out/moyai_tray.svg`、`out/moyai_tray_*.png` |
| ダークのタスクバー用 | 同じ黒縁・白塗りの結び目 | `out/moyai_tray_white.svg`、`out/moyai_tray_white_*.png` |

全サイズで太い黒縁と白い塗りを使い、ロープの外側は透明です。ロープ端を短くして、結び目を中心にしています。

ICO ファイル:

- `out/moyai.ico`: 16、24、32、48、64、128、256px
- `out/moyai_tray.ico`、`out/moyai_tray_white.ico`: 16、20、24、32px

## 製品での使用箇所

`src/Moyai.Tray/Assets/` に 3 つの ICO ファイルを複製して使っています。

- `moyai.ico`: `Moyai.Tray.exe` のアイコン、各ウィンドウのタイトルバー、インストール済みアプリ一覧（`installer/Moyai.wxs` の `ARPPRODUCTICON`）
- `moyai_tray.ico` / `moyai_tray_white.ico`: 通知領域のアイコン。ライト用・ダーク用とも黒縁と白塗りを使います。既存の配色別ファイル名は維持しています。

## 作り直し方

Python 3 と Pillow、NumPy が必要です。

```powershell
python docs\design\icon\make_icon.py
```

`docs\design\icon\out\` 以下が作り直されます。製品へ反映する場合は、`out\` の `moyai.ico`、`moyai_tray.ico`、`moyai_tray_white.ico` を `src\Moyai.Tray\Assets\` へ複製してください。

形状は `make_icon.py` の `knot_rope()` で生成し、`ROPE`（アプリアイコン）と `TRAY_ROPES`（小サイズ）で共用しています。各点の3番目の値は重なり順で、大きいほど上を通ります。
