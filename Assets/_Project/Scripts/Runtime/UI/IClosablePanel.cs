namespace ProjectVita.UI
{
    /// <summary>Esc로 닫을 수 있는 창. 일시정지 메뉴가 메뉴를 열기 전에 먼저 닫는다 (PauseMenu.extraPanels).</summary>
    public interface IClosablePanel
    {
        bool IsOpen { get; }
        void Close();
    }
}
