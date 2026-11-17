using System;
using Pigeon.Movement;

namespace MikuGlider;

// Owns only the decoded resource and this feature's Wwise playing ID.
internal sealed class DalekPlayback
{
    private MonoPcmWave wave;
    private uint playingId;

    internal uint Play()
    {
        if (PlayerLook.Instance == null)
            throw new InvalidOperationException("Local audio emitter is unavailable.");
        if (wave == null)
        {
            using var resource = new EmbeddedResources("MikuGlider.Audio").Open("DalekExterminate", 4 * 1024 * 1024);
            wave = MonoPcmWave.Read(resource);
        }
        Stop();
        var playback = new MonoPcmPlayback(wave);
        // Existing Main/Menu-bank Audio Input event; PCM stays on the game's mixer.
        playingId = AkAudioInputManager.PostAudioInputEvent("Play_VideoAudio",
            PlayerLook.Instance.gameObject, playback.Fill, SetFormat);
        return playingId;
    }

    private static void SetFormat(uint id, AkAudioFormat format)
    {
        format.channelConfig.uNumChannels = 1;
        format.channelConfig.eConfigType = 1;
        format.channelConfig.uChannelMask = 4;
        format.uSampleRate = MonoPcmWave.SampleRate;
    }

    internal void Stop()
    {
        uint id = playingId;
        playingId = 0;
        if (id != 0) AkUnitySoundEngine.StopPlayingID(id);
    }
}
