---
status: accepted
---

# 既定の出力デバイスの切替には非公開の IPolicyConfig を使い、失敗時は OS の設定ページへ退避する

AudioControl で出力デバイスを 1 クリックで切り替えるには、既定デバイスを変更する手段が要る。Core Audio の公開 API（IMMDeviceEnumerator、IAudioEndpointVolume）は列挙・音量・ミュートまでは提供するが、既定デバイスの変更は提供していない。広く使われている手段は非公開の COM インターフェース IPolicyConfig で、作者の別ツール（sound-selector）で実運用の実績がある。ADR-0002 では非公開 API を避ける方針を採ったが、ここでは「1 クリックで切り替わる」ことが機能の本質であり代替が無いため、例外として採用する。

## Consequences

- 非公開依存は AudioControl の既定切替の 1 箇所に閉じ込め、失敗（インターフェース取得不可、HRESULT エラー）時は通知のうえ ms-settings:sound を開く退避を提供する。列挙・音量・ミュート・デバイス変化の通知は公開 API のみで動くので、切替が壊れても操作盤自体は残る。
- Windows の更新で動かなくなった場合、修正の対象はこの 1 箇所に限られる。
- ADR-0002 の方針は維持する。非公開 API を使う箇所を増やす場合は、同様に「代替が無く機能の本質である」ことと退避先を ADR に記す。
