using DG.Tweening;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoldCoinView : MonoBehaviour
{
    Sequence spinSequence;
    [SerializeField] RectTransform coinRect;
    Vector2 startPos;

    private void Awake()
    {
        startPos = coinRect.localPosition;
    }

    public void SetPosition()
    {
        coinRect.localPosition = startPos;
    }

    private void OnEnable()
    {
        spinSequence = DOTween.Sequence();

        spinSequence.Append(coinRect.DOLocalRotate(new Vector3(0, 360, 0), 1, RotateMode.LocalAxisAdd).SetLoops(200, LoopType.Incremental));

        spinSequence.Play();

    }

    private void OnDisable()
    {
        if(spinSequence != null)
            spinSequence.Kill();
    }

    internal void FlyToBalance(Vector3 target, Action done)
    {
        coinRect.DOMove(target, 1).SetEase(Ease.InCirc).OnComplete(()=>
        {
            Taptic.Light();
            SoundsManager.Instance.PlayCoinReachBalance();
            gameObject.SetActive(false);
            done?.Invoke();
        });
    }
}
