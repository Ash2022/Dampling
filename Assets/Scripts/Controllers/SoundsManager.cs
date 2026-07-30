
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class SoundsManager : MonoBehaviour
{
    public enum TapticsStrenght
    {
        Light,
        Medium,
        High
    }

    [SerializeField] AudioClip _coinBalance;
    [SerializeField] AudioClip _illegalMove;
    [SerializeField] AudioClip _levelComplete;
    [SerializeField] AudioClip _levelFail;

    [SerializeField] AudioClip _unitPlayed;
    [SerializeField] AudioClip _linkBroken;
    [SerializeField] AudioClip _hiddenRevealed;
    [SerializeField] AudioClip _keyAndLock;
    [SerializeField] AudioClip _pipeEmit;
    [SerializeField] AudioClip _iceCracked;
    [SerializeField] AudioClip _winkHappen;
    [SerializeField] AudioClip _containerResolved;
    [SerializeField] AudioClip _revive;
    [SerializeField] AudioClip _unitUnlocked;
    [SerializeField] AudioClip _ballJumpToSlot;
    [SerializeField] AudioClip _lidPopped;

    [SerializeField] AudioClip _ballJumpToContainer;

    [SerializeField] AudioClip _boosterButtonClicked;
    [SerializeField] AudioClip _boosterButtonOff;

    [SerializeField] AudioClip _levelCompleteBG;
    [SerializeField] AudioClip _levelCompleteChars;
    


    [SerializeField] List<AudioClip> bgMusics = new List<AudioClip>();


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
    [SerializeField] AudioSource _BGMusic = null;

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

    public void IllegalMove()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_illegalMove);
    }
    internal void PlayCoinReachBalance()
    {
        PlayHaptics(TapticsStrenght.Light);
        PlayClip(_coinBalance, 0.5f);
    }

    public void UnitPlayed()
    {
        PlayHaptics(TapticsStrenght.High);
        PlayClip(_unitPlayed);
    }

    public void LinkBroken()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_linkBroken);
    }

    public void HiddenRevealed()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_hiddenRevealed);
    }

    public void KeyAndLock()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_keyAndLock);
    }

    public void PipeEmit()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_pipeEmit);
    }

    public void IceCracked()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_iceCracked);
    }

    public void WinkHappen()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_winkHappen);
    }

    public void ContainerResolved()
    {
        PlayHaptics(TapticsStrenght.Light);
        PlayClip(_containerResolved);
    }

    public void BallJumpToContainer()
    {
        PlayHaptics(TapticsStrenght.Light);
        PlayClip(_ballJumpToContainer);
    }


    internal void PlayRevive()
    {
        PlayClip(_revive);
    }

    internal void SomethingUnlocked()
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(_unitUnlocked);
    }

    public void BallJumpedToSlot()
    {
        PlayHaptics(TapticsStrenght.Light);
        PlayClip(_ballJumpToSlot);
    }

    public void LidPopped()
    {
        PlayHaptics(TapticsStrenght.Light);
        PlayClip(_lidPopped);
    }

    public void BoosterClicked(bool isOn)
    {
        PlayHaptics(TapticsStrenght.Medium);
        PlayClip(isOn ? _boosterButtonClicked : _boosterButtonOff);
    }

    public void PlayLevelCompleteBG()
    {
        PlayClip(_levelCompleteBG);
    }

    public void PlayLevelCompleteChars()
    {
        PlayClip(_levelCompleteChars);
    }


    public void PlayRandomBackgroundMusic(float fadeDuration = 1f)
    {
        if (bgMusics.Count == 0) return;

        if (_BGMusic.isPlaying)
            _BGMusic.Stop();

        AudioClip randomClip = bgMusics[UnityEngine.Random.Range(0, bgMusics.Count)];
        _BGMusic.clip = randomClip;
        _BGMusic.loop = true;
        _BGMusic.time = randomClip.length * 0.5f;
        _BGMusic.volume = 0f;
        _BGMusic.Play();

        _BGMusic.DOFade(.5f, fadeDuration);
    }

    public void StopBackgroundMusic(float fadeDuration = 1f)
    {
        _BGMusic.DOFade(0f, fadeDuration).OnComplete(() =>
        {
            _BGMusic.Stop();
        });
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
        _BGMusic.mute = mute;

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
        else if (tapticsStrenght == TapticsStrenght.Medium)
            Taptic.Medium();
        else if (tapticsStrenght == TapticsStrenght.High)
            Taptic.Heavy();
    }


}
