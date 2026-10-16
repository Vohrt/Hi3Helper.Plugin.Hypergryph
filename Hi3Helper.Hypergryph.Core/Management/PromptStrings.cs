using System;
using Hi3Helper.Plugin.Core;

namespace Hi3Helper.Hypergryph.Core.Management;

internal sealed class PromptStrings
{
    internal required string Caption                { get; init; }
    internal required string DownloadAsk            { get; init; }
    internal required string PickAsk                { get; init; }
    internal required string PickAskNoPackage       { get; init; }
    internal required string InvalidLauncher        { get; init; }
    internal required string PickerTitle            { get; init; }
    internal required string PickerFilterLauncher   { get; init; }
    internal required string PickerFilterAllFiles   { get; init; }
    internal required string ProgressCaption        { get; init; }
    internal required string ProgressDownloading    { get; init; }
    internal required string ProgressVerifying      { get; init; }
    internal required string ProgressExtracting     { get; init; }
    internal required string ComponentReady         { get; init; }
    internal required string DownloadFailed         { get; init; }

    internal static readonly PromptStrings Chinese = new()
    {
        Caption = "Arknights: Endfield",
        DownloadAsk = """
                      {0} 要求由官方启动器创建游戏进程，但本机没有可用的官方启动器组件。

                      首次启动需要下载官方启动器组件（约 {1:F1} MB）。组件只放在本机，不会在系统里安装官方启动器。

                      是否现在下载？

                      　是(Y)　立即下载
                      　否(N)　改为手动选取已安装的官方启动器
                      　取消　　取消启动
                      """,
        PickAsk = """
                  是否改为手动选取已安装的官方启动器？

                  （在弹出的文件选择框里选中 Games.exe，选中后会被记住，以后不再询问）

                  　是(Y)　打开文件选择
                  　否(N)　取消启动
                  """,
        PickAskNoPackage = """
                           {0} 要求由官方启动器创建游戏进程，但本机没有可用的官方启动器组件。

                           当前区域拿不到官方接口的启动器下载包，只能选取本机已安装的官方启动器。

                           （在弹出的文件选择框里选中 Games.exe，选中后会被记住，以后不再询问）

                           是否手动选取？

                           　是(Y)　打开文件选择
                           　否(N)　取消启动
                           """,
        InvalidLauncher = """
                          所选文件不是可用的官方启动器组件。

                          文件：
                          {0}

                          它必须是一份由鹰角签名的官方 Games.exe。请重新选择，或在文件选择框里点击「取消」放弃启动。

                          　确定　重新选择
                          """,
        PickerTitle = "请选择官方启动器组件 Games.exe（选择后会被记住）",
        PickerFilterLauncher = "官方启动器组件 (Games.exe)",
        PickerFilterAllFiles = "所有文件 (*.*)",
        ProgressCaption = "官方启动器组件",
        ProgressDownloading = "正在下载官方启动器组件… {0}%",
        ProgressVerifying = "正在校验下载文件…",
        ProgressExtracting = "正在解压…",
        ComponentReady = """
                         官方启动器组件已就绪。

                         请再次点击「开始游戏」来启动游戏。
                         """,
        DownloadFailed = """
                         官方启动器组件下载失败。

                         请检查网络连接后重试，或在下次启动时改用手动选取。
                         """
    };

    internal static readonly PromptStrings Japanese = new()
    {
        Caption = "Arknights: Endfield",
        DownloadAsk = """
                      {0} はゲームプロセスを公式ランチャー経由で起動する必要がありますが、本機に使用可能な公式ランチャーコンポーネントが見つかりません。

                      初回起動時は公式ランチャーコンポーネント（約 {1:F1} MB）をダウンロードする必要があります。コンポーネントは本機内にのみ保存され、システムに公式ランチャーがインストールされることはありません。

                      今すぐダウンロードしますか？

                      　はい(Y)　今すぐダウンロード
                      　いいえ(N)　インストール済みの公式ランチャーを手動で選択
                      　キャンセル　起動を中止
                      """,
        PickAsk = """
                  インストール済みの公式ランチャーを手動で選択しますか？

                  （開いたファイル選択ダイアログで Games.exe を選んでください。選択内容は記憶され、次回以降は確認されません）

                  　はい(Y)　ファイルを選択
                  　いいえ(N)　起動を中止
                  """,
        PickAskNoPackage = """
                           {0} はゲームプロセスを公式ランチャー経由で起動する必要がありますが、本機に使用可能な公式ランチャーコンポーネントが見つかりません。

                           現在の地域では公式 API からランチャーのダウンロードパッケージを取得できないため、本機にインストール済みの公式ランチャーを選択してください。

                           （開いたファイル選択ダイアログで Games.exe を選んでください。選択内容は記憶され、次回以降は確認されません）

                           手動で選択しますか？

                           　はい(Y)　ファイルを選択
                           　いいえ(N)　起動を中止
                           """,
        InvalidLauncher = """
                          選択されたファイルは使用可能な公式ランチャーコンポーネントではありません。

                          ファイル:
                          {0}

                          Hypergryph によって署名された公式の Games.exe である必要があります。もう一度選択するか、ファイル選択ダイアログで「キャンセル」を押して起動を中止してください。

                          　OK　再選択
                          """,
        PickerTitle = "公式ランチャーコンポーネント Games.exe を選択（選択内容は記憶されます）",
        PickerFilterLauncher = "公式ランチャーコンポーネント (Games.exe)",
        PickerFilterAllFiles = "すべてのファイル (*.*)",
        ProgressCaption = "公式ランチャーコンポーネント",
        ProgressDownloading = "公式ランチャーコンポーネントをダウンロード中… {0}%",
        ProgressVerifying = "ダウンロードしたファイルを検証中…",
        ProgressExtracting = "展開中…",
        ComponentReady = """
                         公式ランチャーコンポーネントの準備ができました。

                         もう一度「ゲームを開始」をクリックしてゲームを起動してください。
                         """,
        DownloadFailed = """
                         公式ランチャーコンポーネントのダウンロードに失敗しました。

                         ネットワーク接続を確認して再試行するか、次回起動時に手動選択をご利用ください。
                         """
    };

    internal static readonly PromptStrings English = new()
    {
        Caption = "Arknights: Endfield",
        DownloadAsk = """
                      {0} requires the game process to be created by the official launcher, but no usable official launcher component was found on this machine.

                      The first launch needs to download the official launcher component (about {1:F1} MB). The component is kept on this machine only; the official launcher will not be installed into your system.

                      Download it now?

                      　Yes (Y)　Download now
                      　No  (N)　Pick an installed official launcher manually
                      　Cancel　　Abort launching
                      """,
        PickAsk = """
                  Pick an installed official launcher manually instead?

                  (In the file dialog that opens, select Games.exe. Your choice will be remembered, so you will not be asked again.)

                  　Yes (Y)　Open the file picker
                  　No  (N)　Abort launching
                  """,
        PickAskNoPackage = """
                           {0} requires the game process to be created by the official launcher, but no usable official launcher component was found on this machine.

                           The launcher package is not available from the official API in this region, so pick an official launcher already installed on this machine.

                           (In the file dialog that opens, select Games.exe. Your choice will be remembered, so you will not be asked again.)

                           Pick it manually?

                           　Yes (Y)　Open the file picker
                           　No  (N)　Abort launching
                           """,
        InvalidLauncher = """
                          The selected file is not a usable official launcher component.

                          File:
                          {0}

                          It must be an official Games.exe signed by Hypergryph. Select it again, or press Cancel in the file dialog to give up launching.

                          　OK　Choose again
                          """,
        PickerTitle = "Select the official launcher component Games.exe (your choice will be remembered)",
        PickerFilterLauncher = "Official launcher component (Games.exe)",
        PickerFilterAllFiles = "All files (*.*)",
        ProgressCaption = "Official launcher component",
        ProgressDownloading = "Downloading the official launcher component... {0}%",
        ProgressVerifying = "Verifying the downloaded file...",
        ProgressExtracting = "Extracting...",
        ComponentReady = """
                         The official launcher component is ready.

                         Click "Start Game" again to launch the game.
                         """,
        DownloadFailed = """
                         Failed to download the official launcher component.

                         Check your network connection and try again, or pick an installed launcher manually on the next launch.
                         """
    };

    internal static PromptStrings Current
    {
        get
        {
            string code = SharedStatic.PluginLocaleCode;

            if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return Chinese;
            }

            return code.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ? Japanese : English;
        }
    }
}
