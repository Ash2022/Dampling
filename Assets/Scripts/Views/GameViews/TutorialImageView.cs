using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TutorialImageView : MonoBehaviour
{
    [SerializeField] Image tutImage;

    [SerializeField] GameObject bgImage;

    [SerializeField]TMP_Text titleText;
    [SerializeField]TMP_Text bodyText;
    


    public void ShowTutorial(Sprite sprite,Sprite auxSprite,string title,string body)
    {
        SoundsManager.Instance.SomethingUnlocked();

        bgImage.SetActive(true);

        tutImage.sprite = sprite;
        tutImage.SetNativeSize();

        titleText.text = title;
        bodyText.text = body;

        gameObject.SetActive(true);
    }

    public void ShowHardLevelIndication(Sprite sprite)
    {
        bgImage.SetActive(false);
        
        titleText.text = string.Empty;
        bodyText.text = string.Empty;

        tutImage.sprite = sprite;
        tutImage.SetNativeSize();
        gameObject.SetActive(true);

    }

    public void HideTutorial()
    {
        gameObject.SetActive(false);
    }

    public void TutrialClicked()
    {
        GameManager.Instance.TutorialClicked();
        HideTutorial();
    }
}
