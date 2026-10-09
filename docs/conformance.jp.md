# 規格への準拠

[English](conformance.md) | 日本語

ivi-cli が実装している規格と、ivi-cli の振る舞いの関係を、領域ごとにまとめます。各項目は、次の 3 種類のどれかです。

- **準拠**：規格が振る舞いを定めていて、ivi-cli はそのとおりに動きます。該当する節を示します。
- **逸脱**：規格が振る舞いを定めていて、ivi-cli は違う動きをします。追跡している Issue を示します。
- **ivi-cli の定め**：規格が何も定めていないので、ivi-cli が決めました。理由を記録した ADR を示します。

どの項目にも、その振る舞いを固定しているテストを示します。テストで固定されていない振る舞いは、テストを足すまでここには載せません。

> この文書は生きた文書です。いまの振る舞いを書き、コードと同じ PR で更新します。英語版の `conformance.md` も同時に更新します。

出典：

- VISA：IVI Foundation VPP-4.3『The VISA Library』Rev 7.2.1（2024-01-04）
- HiSLIP：IVI Foundation IVI-6.1『High-Speed LAN Instrument Protocol』Rev 2.0（2020-04-23）
- VXI-11：VXIbus Consortium『TCP/IP Instrument Protocol Specification』Rev 1.0（1995-07-17）

## デバイスのタイムアウト

デバイスの `timeout_ms` は、1 つの操作にかけてよい時間です。それぞれの決まりをなぜ選んだかは、[ADR 0053](adr/0053-device-timeout.md) にあります。

### すべてのバックエンド

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| ivi-cli の定め | 書き込み・問い合わせ・読み取り・トリガは、`timeout_ms` を過ぎると `TransportTimeout` で失敗します。 | [DeviceTimeoutBackendFactoryTests][dt]、[ClientTimeoutTests][ct] |
| ivi-cli の定め | タイムアウトのあとはセッションを閉じ、次の操作の前に開き直します。遅れて届いた応答を、あとの要求への答えとして読まないためです。閉じる処理も、操作に許した時間を超えたら打ち切ります。 | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli の定め | セッションを開く処理には、`timeout_ms` と 5 秒の長いほうをかけてよいとします。 | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli の定め | 自分でタイムアウトを守るバックエンド（VXI-11 と VISA）には、ivi-cli が割って入る前に、`timeout_ms` に加えて 1 秒の猶予を与えます。バックエンド自身のタイムアウトのエラーが先に届くようにするためです。 | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli の定め | 呼び出し側によるキャンセルは、タイムアウトではなくキャンセルとして伝えます。サービスリクエストの受信には期限を付けません。 | [DeviceTimeoutBackendFactoryTests][dt] |
| ivi-cli の定め | `visa add` の `timeout_ms` の既定値は 3000ms です。VISA の `VI_ATTR_TMO_VALUE` の既定値は 2000ms です（VPP-4.3 付録 B）。 | — |

### VISA ランタイム（Local バックエンド）

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| 準拠 | セッションの `VI_ATTR_TMO_VALUE`（操作が待つ最小の時間、ミリ秒）に `timeout_ms` を設定します（VPP-4.3 §5.1.2）。 | [LocalBackendTimeoutTests][lt] |
| 準拠 | ランタイムが返す `VI_ERROR_TMO` は、`TransportTimeout` として伝えます（VPP-4.3 §6.1.1）。 | [LocalBackendTimeoutTests][lt] |
| ivi-cli の定め | `viOpen` のタイムアウトを、ロックの取得だけでなくセッションを開く処理にも使います。VPP-4.3 はこれを許していますが、求めてはいません（§4.3.3.2、PERMISSION 4.3.2）。 | [LocalBackendTimeoutTests][lt] |
| ivi-cli の定め | `VI_ERROR_TMO` のあとはセッションを開き直します（上記）。VPP-4.3 は、タイムアウト後のセッションの状態について何も定めておらず、デバイスクリアも求めていません。 | [DeviceTimeoutBackendFactoryTests][dt] |

### VXI-11 クライアント

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| 準拠 | `device_write`・`device_read`・`device_trigger` の `io_timeout` として `timeout_ms` を送ります。この期限はサーバーが守ります（VXI-11 B.4.3、B.5.4）。 | [ClientTimeoutTests][ct] |
| 準拠 | クライアント自身の期限は、`io_timeout` より 1 秒長くします。VXI-11 は、クライアントの期限を `io_timeout` と `lock_timeout` の和より長くするよう求めています（B.4.3）。 | [DeviceTimeoutBackendFactoryTests][dt] |
| 準拠 | サーバーが返すエラー 15 は、`TransportTimeout` として伝えます（B.6.3、B.6.4）。 | [ClientTimeoutTests][ct] |
| ivi-cli の定め | タイムアウトのあとはリンクを破棄し、次の操作の前に作り直します。VXI-11 は、エラー 15 のあとのリンクの状態を定めていません。 | [DeviceTimeoutBackendFactoryTests][dt] |

### VXI-11 ゲートウェイ

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| 準拠 | ゲートウェイの後ろの機器がタイムアウトしたら、クライアントにエラー 15 を返します（B.6.3）。 | [ClientTimeoutTests][ct] |
| 逸脱 | クライアントが送った `io_timeout` を守りません。VXI-11 は、それを超えた呼び出しをサーバーがエラー 15 で終えることを求めています（B.4.3、B.6.3、B.6.4）。[#249](https://github.com/ShortArrow/ivi-cli/issues/249) で追跡しています。 | — |

### HiSLIP クライアント

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| ivi-cli の定め | `timeout_ms` で各操作に期限を付けます。IVI-6.1 にはクライアントの読み書きのタイムアウトがなく、期限が定められているのはロックの待ち時間だけです（§2.6、§6.5）。 | [ClientTimeoutTests][ct] |
| ivi-cli の定め | タイムアウトのあとは、デバイスクリア（§6.12）で同期を取り直すのではなく、接続を閉じて、次の操作の前に開き直します。接続を閉じると、その接続が持っていたロックは解放されます（§2.6）。 | [DeviceTimeoutBackendFactoryTests][dt] |

### SOCKET クライアント

| 種類 | 振る舞い | テスト |
| --- | --- | --- |
| ivi-cli の定め | 生の SOCKET 接続を扱う規格はありません。`timeout_ms` で各操作に期限を付け、タイムアウトのあとは接続を開き直します。 | [ClientTimeoutTests][ct] |

[dt]: ../tests/IviCli.Application.Tests/Backends/DeviceTimeoutBackendFactoryTests.cs
[ct]: ../tests/IviCli.Server.Tests/ClientTimeoutTests.cs
[lt]: ../tests/IviCli.Backends.Local.Tests/LocalBackendTests.cs
