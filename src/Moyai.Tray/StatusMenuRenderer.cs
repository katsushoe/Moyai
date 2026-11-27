namespace Moyai.Tray;

/// <summary>クリック不可の状態行でもLEDの色をそのまま表示します。</summary>
internal sealed class StatusMenuRenderer : ToolStripProfessionalRenderer
{
    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item.Name == "serverStateItem" && e.Image is not null)
            e.Graphics.DrawImage(e.Image, e.ImageRectangle);
        else base.OnRenderItemImage(e);
    }
}
