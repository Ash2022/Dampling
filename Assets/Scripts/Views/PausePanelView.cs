using System;
using UnityEngine;

/// <summary>
/// game pause menu view
/// </summary>
public class PausePanelView : MonoBehaviour
{

    [SerializeField] GameObject _audioOffIndication;
    [SerializeField] GameObject _areYouSureGroup;

    public enum PauseAnswer
    {
        Resume,
        Restart,
        LevelsMap,
        DeleteAll
    }

    Action<PauseAnswer> _playOrMenu;

    bool _audioOff = false;

    /// <summary>
    /// shows the panel 
    /// </summary>
    /// <param name="playOrMenu">call back to send back the selected option</param>
    public void ShowPanel(Action<PauseAnswer> playOrMenu)
    {
        _playOrMenu = playOrMenu;
        gameObject.SetActive(true);
    }

    public void PausePanelPlayClicked()
    {
        _playOrMenu(PauseAnswer.Resume);
    }

    public void PausePanelMenuClicked()
    {
        _playOrMenu(PauseAnswer.LevelsMap);
    }

    public void PausePanelRestartClicked()
    {
        _playOrMenu(PauseAnswer.Restart);
    }

    public void PausePanelDeleteAllClicked()
    {
        _areYouSureGroup.SetActive(true);
    }

    //hides the panel
    public void HidePanel()
    {
        gameObject.SetActive(false);
        _areYouSureGroup.SetActive(false);
    }

    public void AudioClicked()
    {
        _audioOff = !_audioOff;

        _audioOffIndication.SetActive(_audioOff);

        SoundsManager.Instance.MuteAll(_audioOff);
    }

    

    public void AreYouSureAnswerClicked(bool yes)
    {
        _areYouSureGroup.SetActive(false);
        
        if(yes)
            _playOrMenu(PauseAnswer.DeleteAll);
        
        
    }

}
