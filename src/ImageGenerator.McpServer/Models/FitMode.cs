namespace ImageGenerator.McpServer.Models;

public enum FitMode
{
    // 目標比率で中央を切り出してから目標サイズへ
    Cover,

    // 目標サイズに内接するよう縮小 (余白なし。サイズは目標以下)
    Contain,

    // 内接させた上で余白を付けて目標サイズにする
    Pad,

    // 比率を無視して目標サイズへ引き伸ばす (resize_imageのみ)
    Stretch
}
