using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.ParticleSystem;

public class GameOverView : MonoBehaviour
{
    [SerializeField] CanvasGroup mainCanvasGroup;
    
    [SerializeField] Button endGameButton;
    [SerializeField] Image buttonImage;

    [SerializeField] Sprite continueButton;
    [SerializeField] Sprite tryAgainButton;

    [SerializeField] GameObject particles;

    [SerializeField] Image endGameBG;

    [SerializeField] GameObject goldCoinsGroup;
    [SerializeField] CanvasGroup goldCoinsCanvasGroup;
    [SerializeField]TMP_Text goldCoinsText;
    [SerializeField] List<GoldCoinView> availableCoinViews;
  
    [SerializeField]Image endGameImage;
    [SerializeField]Sprite loseSprite;
    [SerializeField]Sprite winSprite;
    


    [SerializeField] EndScreenUnlockView endScreenUnlockView;
    int currLevelIndex;
    bool noMoreUnlocks;
    Action endGameComplete;

    Coroutine goldCoinsRoutine;

    public void InitEndScreen(bool levelWon, int levelIndex, Action endGameResumeClicked)
    {

        endGameImage.sprite = levelWon?winSprite:loseSprite;

        endGameImage.SetNativeSize();

        currLevelIndex = levelIndex;
        endScreenUnlockView.BackParticles.SetActive(false);
        endScreenUnlockView.CanvasGroup.alpha = 0;

        mainCanvasGroup.alpha = 0;

        particles.SetActive(false);
        gameObject.SetActive(true);

        goldCoinsGroup.SetActive(false);

        endGameComplete = endGameResumeClicked;

        endGameButton.interactable = false;
        buttonImage.sprite = levelWon ? continueButton : tryAgainButton;
        buttonImage.gameObject.SetActive(false );

        bool unlocksFinished = UnlocksFinished();

        endGameBG.color = new Color(0, 0, 0, 0.95f);

        if (levelWon)
        {
            goldCoinsCanvasGroup.alpha = 0;
            goldCoinsGroup.SetActive(true);
            goldCoinsText.text = ModelManager.GOLD_PER_WIN.ToString();
        }
        else
        {
            endScreenUnlockView.HideEndScreenUnlocks();
            goldCoinsGroup.SetActive(false);
        }

        SoundsManager.Instance.PlayHaptics(SoundsManager.TapticsStrenght.Medium);

        mainCanvasGroup.DOFade(1, 1).SetDelay(levelWon?0.35f:1f).OnComplete(() =>
        {

            if (levelWon)
            {
                particles.SetActive(true); 

                SoundsManager.Instance.PlayLevelCompelte();

                InitEndScreenUnlockView();

                if (!noMoreUnlocks)
                {
                    ProgressUnlockView(()=>
                    {
                        goldCoinsRoutine = StartCoroutine(GiveGoldCoinsAndEnableContinue(levelWon));
                    });
                }
                else
                    goldCoinsRoutine = StartCoroutine(GiveGoldCoinsAndEnableContinue(levelWon));
            }
            else
                {
                    SoundsManager.Instance.PlayLevelFailed();
                    EnableContinue();
                }
        });
    }

    private IEnumerator GiveGoldCoinsAndEnableContinue(bool levelWon)
    {
        //fly gold coins if applicable and enable continue

        if(levelWon)
        {
            goldCoinsCanvasGroup.DOFade(1, 0.1f);

            GameManager.Instance.MoveBalanceUp();

            for (int i = 0; i < 10; i++)
            {
                availableCoinViews[i].gameObject.GetComponent<RectTransform>().localPosition = new Vector3(-85, 0, 0);

                availableCoinViews[i].gameObject.SetActive(true);

                availableCoinViews[i].FlyToBalance(GameManager.Instance.GetBalanceRect(),()=>
                {
                    //fly the money to the balance (model already updtaed)
                    GameManager.Instance.AddToBalanceVisual(ModelManager.GOLD_PER_WIN/10);
                });

                yield return new WaitForSeconds(0.1f);

                if (i == 9)
                {
                    EnableContinue();

                }
            }
        }
        else
        {
            EnableContinue();
        }

    }

    private void EnableContinue()
    {
        endGameButton.interactable = true;
        buttonImage.gameObject.SetActive(true);
    }

    public void EndGameButtonClicked()
    {
        GameManager.Instance.SetUIToBalance();

        gameObject.SetActive(false);
        endGameComplete?.Invoke();
    }


    private bool UnlocksFinished()
    {
        List<int> unlocksIndexList = ModelManager.Instance.UnlocksIndexList;

        //now i need to see where i am in this list

        int startIndex = 0;
        int endIndex = 0;
        int presentIndex = 0;

        if (unlocksIndexList != null)
        {
            for (int i = 0; i < unlocksIndexList.Count; i++)
            {
                if (currLevelIndex < unlocksIndexList[i] && endIndex == 0)
                {
                    endIndex = unlocksIndexList[i];
                    presentIndex = i;

                    if (i > 0)
                        startIndex = unlocksIndexList[i - 1];

                }

            }
        }

        return endIndex == 0;   
    }


    private void InitEndScreenUnlockView()
    {
        //init to show the current state

        //if win - need to increase the progress bar - need to update the text value

        //need to check the level i am on - then see all the unlocks - see where i am - set the total and current steps

        List<int> unlocksIndexList = ModelManager.Instance.UnlocksIndexList;

        //now i need to see where i am in this list

        int startIndex = 0;
        int endIndex = 0;
        int presentIndex = 0;

        if(unlocksIndexList != null)
        {
            for (int i = 0; i < unlocksIndexList.Count; i++)
            {
                if (currLevelIndex < unlocksIndexList[i] && endIndex == 0)
                {
                    endIndex = unlocksIndexList[i];
                    presentIndex = i;

                    if (i > 0)
                        startIndex = unlocksIndexList[i - 1];

                }

            }
        }


        //if end index == 0 -- no more unlocks to show

        if (endIndex == 0)
        {
            noMoreUnlocks = true;
            endScreenUnlockView.HideEndScreenUnlocks();
        }
        else
        {

            int total = endIndex - startIndex;
            int curr = currLevelIndex - startIndex;

            Debug.Log("presentIndex " + presentIndex);

            Color unlockColor = Color.white;

            //endScreenUnlockView.InitDisplay(unlockColor, total, curr);
            

            endScreenUnlockView.InitDisplay(VisualsManager.Instance.GetUnlockImage(presentIndex), VisualsManager.Instance.GetUnlockImage(presentIndex),total, curr);
        }


    }
    private void ProgressUnlockView(Action done)
    {
        //need to see if need to update the model for powerUps 
        //SoundsController.Instance.PlayProgressBar();
        endScreenUnlockView.UpdateProgress(done);
    }
}
