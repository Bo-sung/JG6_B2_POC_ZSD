public sealed class BuildPanelPresenter
{
    readonly BuildPanelView view;
    readonly GameSession session;
    readonly BuildController controller;

    public BuildPanelPresenter(BuildPanelView _view, GameSession _session, BuildController _controller)
    {
        view = _view;
        session = _session;
        controller = _controller;
        view.WallClicked += () => controller.Begin(BuildController.Mode.Wall);
        view.TowerClicked += () => controller.Begin(BuildController.Mode.Tower);
        view.RepairClicked += () => controller.Begin(BuildController.Mode.Repair);
        view.CancelClicked += controller.Cancel;
    }

    public void Refresh()
    {
        view.Show(session.BuildingCost(BuildingType.Wall), session.BuildingCost(BuildingType.Tower), session.CanBuild, controller.Message);
    }
}
