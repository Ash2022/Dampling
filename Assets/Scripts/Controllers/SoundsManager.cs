
using System;
using System.Collections.Generic;
using UnityEngine;

public class SoundsManager : MonoBehaviour
{
    public enum TapticsStrenght
    {
        Light,
        Medium,
        High
    }

    [SerializeField] AudioClip _containerPlayed;
    [SerializeField] AudioClip _illegalMove;

    [SerializeField] AudioClip _bubbleShotToContainer;
    [SerializeField] AudioClip _bubbleArrived;

    [SerializeField] AudioClip _unlockedAchieved;

    [SerializeField] AudioClip _bubbleemitted;
    [SerializeField] AudioClip _hiddenUnlocked;

    [SerializeField] AudioClip _revive;
    [SerializeField] AudioClip _pushButton1;
    [SerializeField] AudioClip _pushButton2;


    [SerializeField] AudioClip _coinBalance;

    [SerializeField] AudioClip _bigBubblePop;

    [SerializeField] AudioClip _levelComplete;
    [SerializeField] AudioClip _levelFail;
    

    [SerializeField] AudioSource _SFX_Source1 = null;
    [SerializeField] AudioSource _SFX_Source2 = null;
    [SerializeField] AudioSource _SFX_Source3 = null;
    [SerializeField] AudioSource _SFX_Source4 = null;
    [SerializeField] AudioSource _SFX_Source5 = null;
    [SerializeField] AudioSource _SFX_Source6 = null;
    [SerializeField] AudioSource _SFX_Source7 = null;
    [SerializeField] AudioSource _SFX_Source8 = null;
    [SerializeField] AudioSource _SFX_Source9 = null;
    [SerializeField] AudioSource _SFX_Source10 = null;
    [SerializeField] AudioSource _SFX_Source11 = null;

    static SoundsManager _instance;

    public static SoundsManager Instance => _instance;

    private void Awake()
    {
        _instance = this;
    }
   

    internal void PlayLevelFailed()
    {
        PlayClip(_levelFail);
        PlayHaptics(TapticsStrenght.Medium);
    }

    public void PlayLevelCompelte()
    {
        PlayClip(_levelComplete);
        PlayHaptics(TapticsStrenght.Medium);
    }

   
    public void PlayContainer()
    {
        PlayClip(_containerPlayed);
    }

    public void IllegalMove()
    {
        PlayClip(_illegalMove);
    }

    public void BubbleMove()
    {
        PlayClip(_bubbleShotToContainer);
    }

    public void BubbleArrived()
    {
        PlayClip(_bubbleArrived);
    }

    public void BubbleEmitted()
    {
        PlayClip(_bubbleemitted);
    }

    public void HiddenRevealed()
    {
        PlayClip(_hiddenUnlocked);
    }

    public void SomethingUnlocked()
    {
        PlayClip(_unlockedAchieved);
    }

    public void PlayRevive()
    {
        PlayClip(_revive);
    }

    public void PlayBigBubblePop()
    {
        PlayClip(_bigBubblePop);
    }

    public void PlayPushButton()
    {
        PlayClip(_pushButton1);
        PlayClip(_pushButton2);
        PlayHaptics(TapticsStrenght.Light);
    }

    public void DisableEnableMixer(bool disable)
    {
        if (disable)
            AudioListener.volume = 0;
        else
            AudioListener.volume = 1f;

    }

    public void MuteAll(bool mute)
    {
        _SFX_Source1.mute = mute;
        _SFX_Source2.mute = mute;
        _SFX_Source3.mute = mute;
        _SFX_Source4.mute = mute;
        _SFX_Source5.mute = mute;
        _SFX_Source6.mute = mute;
        _SFX_Source7.mute = mute;
        _SFX_Source8.mute = mute;
        _SFX_Source9.mute = mute;
        _SFX_Source10.mute = mute;
        _SFX_Source11.mute = mute;

    }


    public AudioSource PlayClip(AudioClip clip, float volume = 1, float pitch = 1)
    {
        AudioSource audio_source = GetFreeAudioSource();

        if (audio_source != null && audio_source.enabled == true)
        {
            audio_source.clip = clip;
            audio_source.pitch = pitch;
            audio_source.volume = volume;
            audio_source.Play();
        }

        return audio_source;
    }



    private AudioSource GetFreeAudioSource()
    {
        if (!_SFX_Source1.isPlaying)
            return _SFX_Source1;

        if (!_SFX_Source2.isPlaying)
            return _SFX_Source2;

        if (!_SFX_Source3.isPlaying)
            return _SFX_Source3;

        if (!_SFX_Source4.isPlaying)
            return _SFX_Source4;

        if (!_SFX_Source5.isPlaying)
            return _SFX_Source5;

        if (!_SFX_Source6.isPlaying)
            return _SFX_Source6;

        if (!_SFX_Source7.isPlaying)
            return _SFX_Source7;

        if (!_SFX_Source8.isPlaying)
            return _SFX_Source8;

        if (!_SFX_Source9.isPlaying)
            return _SFX_Source9;

        if (!_SFX_Source10.isPlaying)
            return _SFX_Source10;

        if (!_SFX_Source11.isPlaying)
            return _SFX_Source11;


        return null;

    }

    //when selecting elevators - medium
    //when selecting shaft - medium
    //people moving - light
    //elevator shots out - hard

    //used to later change between IOS and Android as needed
    public void PlayHaptics(TapticsStrenght tapticsStrenght)
    {
        
        if (tapticsStrenght == TapticsStrenght.Light)
            Taptic.Light();
        else if(tapticsStrenght == TapticsStrenght.Medium)
            Taptic.Medium();
        else if(tapticsStrenght == TapticsStrenght.High)
            Taptic.Heavy();
    }

    internal void PlayCoinReachBalance()
    {
        PlayClip(_coinBalance,0.5f);
    }
}
