public interface IMenuPanel
{
    void OnEnter();            // Called when menu becomes active
    void OnExit();             // Called when menu is exited
    bool CanLeave();           // Whether it's safe to leave this menu
}