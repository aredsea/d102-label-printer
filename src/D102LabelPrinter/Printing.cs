using System.Windows.Forms;

namespace D102LabelPrinter;

public static class Printing
{
    /// <summary>현재 레이아웃으로 샘플 라벨 1장을 실제 인쇄(위치 확인용).</summary>
    public static void TestPrint(IWin32Window owner = null)
    {
        try
        {
            var item = new LabelItem
            {
                Barcode = "2606RL", ItemName = "F-볼륨하트언발체인", ItemNo = "F-BF-Q-YG-ZZ-0096",
                Price = 1830000, Metal = "18K", Diameter = "17", Weight = "4.36g",
                Category = "FASHION", Partner = "백*심9932/G"
            };
            string zpl = ZplEngine.BuildLabel(item, AppState.Layout, AppState.Config);
            RawPrinter.SendZpl(AppState.Config.PrinterName, zpl);
            MessageBox.Show(owner, "테스트 라벨을 인쇄했습니다.", "D102 라벨 인쇄",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show(owner, "테스트 인쇄 실패:\n" + e.Message, "D102 라벨 인쇄",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
