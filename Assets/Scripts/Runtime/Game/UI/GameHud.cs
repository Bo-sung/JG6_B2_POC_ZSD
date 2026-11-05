using UnityEngine;

// Hud 프리팹의 루트. View와 Presenter를 잇고 매 프레임 표시를 갱신한다.
[DisallowMultipleComponent]
public sealed class GameHud : MonoBehaviour
{
    [SerializeField] GameSession session;
    [SerializeField] RTSInput input;
    [SerializeField] HudView hudView;
    [SerializeField] SurvivorListView listView;
    [SerializeField] SelectionPanelView selectionView;
    [SerializeField] AlertBannerView alertView;
    [SerializeField] ResultPanelView resultView;

    HudPresenter hud;
    SurvivorListPresenter list;
    SelectionPanelPresenter selection;
    AlertPresenter alert;
    ResultPresenter result;

    // GameSession.Awake 이후에 세션 상태를 읽는다.
    void Start()
    {
        hud = new HudPresenter(hudView, session);
        list = new SurvivorListPresenter(listView, session, input);
        selection = new SelectionPanelPresenter(selectionView, session, input);
        alert = new AlertPresenter(alertView, session);
        result = new ResultPresenter(resultView, session);
        GameEvents.SurvivorDied += alert.OnSurvivorDied;
    }

    void OnDestroy()
    {
        if (alert != null)
        {
            GameEvents.SurvivorDied -= alert.OnSurvivorDied;
        }
    }

    void Update()
    {
        hud.Refresh();
        list.Refresh();
        selection.Refresh();
        alert.Refresh();
        result.Refresh();
    }
}
