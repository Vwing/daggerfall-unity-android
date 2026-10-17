// Project:         Daggerfall Unity
// Copyright:       Copyright (C) 2009-2023 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:
//
// Notes:
//

using UnityEngine;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.AudioSynthesis.Bank;
using DaggerfallWorkshop.AudioSynthesis.Sequencer;
using DaggerfallWorkshop.AudioSynthesis.Synthesis;
using DaggerfallWorkshop.AudioSynthesis.Midi;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine.Audio;

namespace DaggerfallWorkshop
{
    [RequireComponent(typeof(AudioSource))]
    public class DaggerfallSongPlayer : MonoBehaviour
    {
        const string sourceFolderName = "SoundFonts";
        const string defaultSoundFontFilename = "TimGM6mb.sf2";
        const int fallbackSampleRate = 48000;
        const int maxSynthSampleRate = 96000;
        const int polyphony = 100;
        const float synthRetryDelay = 10f;

        private static AudioMixerGroup _defaultAudioMixerGroup;

        [NonSerialized, HideInInspector]
        public bool IsPlaying = false;
        [NonSerialized, HideInInspector]
        public int CurrentTime = 0;
        [NonSerialized, HideInInspector]
        public int EndTime = 0;

        public bool ShowDebugString = false;

        [Range(0.0f, 10.0f)]
        public float Gain = 5.0f;
        private bool IsMuted = false;
        public string SongFolder = "Songs/";
        public SongFiles Song = SongFiles.song_none;

        AudioSource audioSource;
        Synthesizer midiSynthesizer = null;
        MidiFileSequencer midiSequencer = null;
        string currentMidiName;
        float[] sampleBuffer = new float[0];
        int sampleBufferPosition = 0;
        int channels = 0;
        int bufferLength = 0;
        int numBuffers = 0;
        int synthSampleRate = 0;
        int synthOutputSampleRate = 0;
        int synthUpsampleFactor = 1;
        int upsampleCounter = 0;
        float nextSynthRetryTime = 0;
        bool loggedSynthFailure = false;
        bool loggedSourceRestart = false;
        bool playEnabled = false;
        bool awakeComplete = false;
        float oldGain;

        bool isImported;
        bool isLoading;     

        /// <summary>
        /// Gets peer AudioSource component.
        /// </summary>
        public AudioSource AudioSource
        {
            get { return (audioSource) ? audioSource : GetComponent<AudioSource>(); }
        }

        void Start()
        {
            DaggerfallVidPlayerWindow.OnVideoStart += DaggerfallVidPlayerWindow_OnVideoStart;
            DaggerfallVidPlayerWindow.OnVideoEnd += DaggerfallVidPlayerWindow_OnVideoEnd;
            AudioSettings.OnAudioConfigurationChanged += AudioSettings_OnAudioConfigurationChanged;

            InitSynth();
        }

        void OnDestroy()
        {
            DaggerfallVidPlayerWindow.OnVideoStart -= DaggerfallVidPlayerWindow_OnVideoStart;
            DaggerfallVidPlayerWindow.OnVideoEnd -= DaggerfallVidPlayerWindow_OnVideoEnd;
            AudioSettings.OnAudioConfigurationChanged -= AudioSettings_OnAudioConfigurationChanged;
        }

        void Update()
        {
            if (!isImported)
            {
                // Update status
                if (midiSequencer != null)
                {
                    IsPlaying = midiSequencer.IsPlaying;
                    CurrentTime = midiSequencer.CurrentTime;
                    EndTime = midiSequencer.EndTime;
                }

                // The sequencer only advances while the AudioSource is playing. If something stops the source
                // (such as the audio output being reset), restart it rather than leaving the song stuck as playing.
                if (playEnabled && IsPlaying && !audioSource.isPlaying)
                {
                    if (!loggedSourceRestart)
                    {
                        Debug.LogFormat("DaggerfallSongPlayer: Music AudioSource stopped while '{0}' was playing; restarting it. {1}", currentMidiName, GetAudioConfigurationString());
                        loggedSourceRestart = true;
                    }
                    audioSource.Play();
                }
            }
            else
            {
                // Start playing
                if (isLoading && audioSource.clip.loadState == AudioDataLoadState.Loaded)
                {
                    isLoading = false;
                    audioSource.Play();
                }

                // Update status
                IsPlaying = audioSource.isPlaying || isLoading;
                CurrentTime = audioSource.timeSamples;
                EndTime = audioSource.clip.samples;
            }
            audioSource.volume = IsMuted ? 0f : DaggerfallUnity.Settings.MusicVolume;
            if (!audioSource.outputAudioMixerGroup)
            {
                if (!_defaultAudioMixerGroup)
                    _defaultAudioMixerGroup = Resources.Load<AudioMixer>("MainMixer").FindMatchingGroups("Music")[0];
                audioSource.outputAudioMixerGroup = _defaultAudioMixerGroup;
            }
        }

        void LateUpdate()
        {
            if (!isImported)
            {
                if (audioSource.playOnAwake && (midiSequencer != null && !midiSequencer.IsPlaying) && !awakeComplete)
                {
                    Play();
                    awakeComplete = true;
                }
                if (audioSource.loop && midiSequencer != null && !midiSequencer.IsPlaying)
                {
                    Play();
                }
            }
        }

        void OnGUI()
        {
            if (Event.current.type.Equals(EventType.Repaint) && ShowDebugString)
            {
                GUIStyle style = new GUIStyle();
                style.normal.textColor = Color.black;
                string text = GetDebugString();
                GUI.Label(new Rect(10, 50, 800, 24), text, style);
                GUI.Label(new Rect(8, 48, 800, 24), text);
            }
        }

        public void Play()
        {
            if (Song == SongFiles.song_none)
                return;

            Play(Song);
        }

        /// <summary>
        /// Play current song.
        /// </summary>
        public void Play(SongFiles song)
        {
            // Stop if playing another song
            Stop();

            // Import custom song. Imported songs don't use the synth, so they still play if it couldn't start.
            AudioClip clip;
            if (isImported = SoundReplacement.TryImportSong(song, out clip))
            {
                Song = song;
                audioSource.clip = clip;
                isLoading = true;
                return;
            }

            if (!InitSynth())
                return;

            // Load song data
            string filename = EnumToFilename(song);
            byte[] songData = LoadSong(filename);
            if (songData == null)
                return;

            // Drop any samples left over from the previous song
            sampleBufferPosition = sampleBuffer.Length;
            upsampleCounter = 0;
            loggedSourceRestart = false;

            // Create song
            MidiFile midiFile = new MidiFile(new MyMemoryFile(songData, filename));
            if (midiSequencer.LoadMidi(midiFile))
            {
                // MIDI playback is generated in OnAudioFilterRead, which only runs
                // while the AudioSource is active. Do not rely on playOnAwake here,
                // as some platforms can leave the source silent during startup.
                if (!audioSource.isPlaying)
                    audioSource.Play();

                midiSequencer.Play();
                currentMidiName = filename;
                playEnabled = true;
                IsPlaying = true;
            }
        }

        /// <summary>
        /// Stop playing song.
        /// </summary>
        public void Stop()
        {
            if (!audioSource)
                audioSource = GetComponent<AudioSource>();

            // Reset audiosource clip
            if (isImported)
            {
                isImported = false;
                audioSource.Stop();
                audioSource.clip = null;
                audioSource.Play();
            }

            // Stop if playing a song
            if (midiSequencer != null && midiSequencer.IsPlaying)
            {
                midiSequencer.Stop();
                midiSynthesizer.NoteOffAll(true);
                midiSynthesizer.ResetSynthControls();
                midiSynthesizer.ResetPrograms();
                playEnabled = false;
            }
        }

        #region Private Methods

        private bool InitSynth()
        {
            // Get peer AudioSource
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
            {
                DaggerfallUnity.LogMessage("DaggerfallSongPlayer: Could not find AudioSource component.");
                return false;
            }

            // Create synthesizer and load bank
            if (midiSynthesizer == null)
            {
                // SongManager asks for a song every frame while nothing plays, so retry a synth that failed to start
                // only every few seconds rather than on every request
                if (Time.realtimeSinceStartup < nextSynthRetryTime)
                    return false;

                try
                {
                    // Get output format from Unity's active audio configuration. Some Android devices
                    // use a non-48 kHz output path, and the synth must match it to keep MIDI timing stable.
                    synthOutputSampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : fallbackSampleRate;
                    // The synth only supports up to 96 kHz, so faster outputs run it at an exact fraction of the output rate
                    // and repeat each of its samples to fill the gap.
                    synthUpsampleFactor = (synthOutputSampleRate + maxSynthSampleRate - 1) / maxSynthSampleRate;
                    synthSampleRate = synthOutputSampleRate / synthUpsampleFactor;
                    channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
                    if (!loggedSynthFailure)
                        Debug.LogFormat("DaggerfallSongPlayer: {0}. Starting MIDI synth at {1} Hz.", GetAudioConfigurationString(), synthSampleRate);

                    // Create synth
                    AudioSettings.GetDSPBufferSize(out bufferLength, out numBuffers);
                    numBuffers = Math.Max(1, numBuffers);
                    Synthesizer synth = new Synthesizer(synthSampleRate, channels, bufferLength / numBuffers, numBuffers, polyphony);

                    // Load bank data
                    string filename = DaggerfallUnity.Settings.SoundFont;
                    byte[] bankData = LoadBank(filename);
                    if (bankData == null)
                    {
                        // Attempt to fallback to default internal soundfont
                        bankData = LoadDefaultSoundFont();
                        filename = defaultSoundFontFilename;
                        Debug.LogFormat("Using default SoundFont {0}", defaultSoundFontFilename);
                    }
                    else
                    {
                        Debug.LogFormat("Trying custom SoundFont {0}", filename);
                    }

                    // Assign to synth
                    if (bankData == null)
                    {
                        nextSynthRetryTime = Time.realtimeSinceStartup + synthRetryDelay;
                        return false;
                    }
                    synth.LoadBank(new MyMemoryFile(bankData, filename));
                    synth.ResetSynthControls(); // Need to do this for bank to load properly, don't know why
                    midiSynthesizer = synth;

                    if (loggedSynthFailure)
                    {
                        Debug.LogFormat("DaggerfallSongPlayer: MIDI synth started at {0} Hz after an earlier failure. {1}", synthSampleRate, GetAudioConfigurationString());
                        loggedSynthFailure = false;
                    }
                }
                catch (Exception ex)
                {
                    // Log only the first failure; retries until one succeeds are silent
                    nextSynthRetryTime = Time.realtimeSinceStartup + synthRetryDelay;
                    if (!loggedSynthFailure)
                    {
                        Debug.LogErrorFormat("DaggerfallSongPlayer: Could not start the MIDI synth, so MIDI music is off. Retrying every {0} seconds. {1}\n{2}", synthRetryDelay, GetAudioConfigurationString(), ex);
                        loggedSynthFailure = true;
                    }
                    return false;
                }
            }

            // Create sequencer
            if (midiSequencer == null)
                midiSequencer = new MidiFileSequencer(midiSynthesizer);

            // Check init
            if (midiSynthesizer == null || midiSequencer == null)
            {
                DaggerfallUnity.LogMessage("DaggerfallSongPlayer: Failed to init synth.");
                return false;
            }

            return true;
        }

        private string EnumToFilename(SongFiles song)
        {
            string enumName = song.ToString();
            return enumName.Remove(0, "song_".Length) + ".mid";
        }

        private byte[] LoadBank(string filename)
        {
            // Do nothing if no filename set
            if (string.IsNullOrEmpty(filename))
                return null;

            // Check file exists
            string path = Path.Combine(Paths.StreamingAssetsPath, sourceFolderName);
            string filePath = Path.Combine(path, filename);
            if (!File.Exists(filePath))
            {
                // Fallback to default sound font
                Debug.LogFormat("Could not find file '{0}', falling back to default soundfont {1}.", filePath, defaultSoundFontFilename);
                return null;
            }

            // Load data
            return File.ReadAllBytes(filePath);
        }

        private byte[] LoadDefaultSoundFont()
        {
            TextAsset asset = Resources.Load<TextAsset>(defaultSoundFontFilename);
            if (asset != null)
            {
                return asset.bytes;
            }

            DaggerfallUnity.LogMessage(string.Format("DaggerfallSongPlayer: Bank file '{0}' not found.", defaultSoundFontFilename));

            return null;
        }

        private byte[] LoadSong(string filename)
        {
            // Get custom midi song
            byte[] songBytes;
            if (SoundReplacement.TryImportMidiSong(filename, out songBytes))
                return songBytes;

            // Get Daggerfal song
            TextAsset asset = Resources.Load<TextAsset>(Path.Combine(SongFolder, filename));
            if (asset != null)
            {
                return asset.bytes;
            }

            DaggerfallUnity.LogMessage(string.Format("DaggerfallSongPlayer: Song file '{0}' not found.", filename));

            return null;
        }

        private string GetDebugString()
        {
            if (midiSequencer == null)
                return "Sequencer not ready.";
            if (midiSynthesizer == null)
                return "Synthesizer not ready.";

            string final;
            if (isImported)
            {
                if (isLoading)
                    final = string.Format("Loading song '{0}'", Song);
                else
                    final = string.Format("Playing song '{0}' at position {1}/{2}", Song, audioSource.timeSamples, audioSource.clip.samples);
            }
            else if (midiSequencer.IsPlaying)
                final = string.Format("Playing song '{0}' at position {1}/{2}", currentMidiName, midiSequencer.CurrentTime, midiSequencer.EndTime);
            else
                final = string.Format("Song '{0}' ready. Not playing.", currentMidiName);

            return final;
        }

        private static string GetAudioConfigurationString()
        {
            AudioSettings.GetDSPBufferSize(out int dspBufferLength, out int dspNumBuffers);
            return string.Format("Audio output {0} Hz, speaker mode {1}, driver {2}, DSP buffer {3} x {4}",
                AudioSettings.outputSampleRate, AudioSettings.speakerMode, AudioSettings.driverCapabilities, dspBufferLength, dspNumBuffers);
        }

        // Unity rebuilds the audio output when the output device or format changes, which stops AudioSources and can change
        // the output sample rate. The synth is built for one sample rate, so rebuild it if that changed; SongManager restarts
        // the song once IsPlaying drops. A stopped source at an unchanged rate is restarted in Update().
        private void AudioSettings_OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            Debug.LogFormat("DaggerfallSongPlayer: Audio configuration changed (device changed: {0}). {1}", deviceWasChanged, GetAudioConfigurationString());

            if (AudioSettings.outputSampleRate == synthOutputSampleRate)
                return;

            playEnabled = false;
            if (!isImported)
            {
                Stop();
                IsPlaying = false;
            }
            midiSynthesizer = null;
            midiSequencer = null;
            nextSynthRetryTime = 0; // retry a failed synth right away at the new rate
        }

        private void DaggerfallVidPlayerWindow_OnVideoStart()
        {
            // Mute music while video is playing
            oldGain = Gain;
            Gain = 0;
            IsMuted = true;
        }

        private void DaggerfallVidPlayerWindow_OnVideoEnd()
        {
            // Restore music to previous level
            Gain = oldGain;
            IsMuted = false;
        }

        #endregion

        #region Audio Filter

        // Called when audio filter needs more sound data
        void OnAudioFilterRead(float[] data, int channels)
        {
            // Do nothing if play not enabled
            // This flag is raised/lowered when user starts/stops play
            // Helps avoids thread finding synth in state of shutting down
            if (!playEnabled)
                return;

            // Must have synth and seq. Use local copies, as the main thread can replace them when the audio output changes.
            Synthesizer synth = midiSynthesizer;
            MidiFileSequencer sequencer = midiSequencer;
            if (synth == null || sequencer == null)
                return;

            // The synth renders mono or stereo; any extra output channels are left silent
            int synthChannels = Math.Min(channels, 2);
            if (synthChannels != synth.AudioChannels)
            {
                synth.SetAudioChannelCount(synthChannels);
                sampleBufferPosition = sampleBuffer.Length; // drop samples rendered with the old channel count
            }

            try
            {
                // Update sequencing - must be playing a song
                if (sequencer.IsMidiLoaded && sequencer.IsPlaying)
                {
                    int frames = data.Length / channels;
                    for (int frame = 0; frame < frames; frame++)
                    {
                        // The synth's working buffer rarely matches Unity's buffer, so render more whenever it runs out
                        if (sampleBufferPosition >= sampleBuffer.Length)
                        {
                            if (sampleBuffer.Length != synth.WorkingBufferSize)
                                sampleBuffer = new float[synth.WorkingBufferSize];
                            sequencer.FillMidiEventQueue();
                            synth.GetNext(sampleBuffer);
                            sampleBufferPosition = 0;
                        }

                        for (int channel = 0; channel < channels; channel++)
                            data[frame * channels + channel] = channel < synthChannels ? sampleBuffer[sampleBufferPosition + channel] * Gain : 0f;

                        if (++upsampleCounter >= synthUpsampleFactor)
                        {
                            upsampleCounter = 0;
                            sampleBufferPosition += synthChannels;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Will sometimes drop here if Unity tries to feed audio filter
                // from another thread while synth is starting up or shutting down
                // Just nom the exception
            }
        }

        #endregion

        #region Interface Implementation

        public class MyMemoryFile : AudioSynthesis.IResource
        {
            private byte[] file;
            private string fileName;
            public MyMemoryFile(byte[] file, string fileName)
            {
                this.file = file;
                this.fileName = fileName;
            }
            public string GetName() { return fileName; }
            public bool DeleteAllowed() { return false; }
            public bool ReadAllowed() { return true; }
            public bool WriteAllowed() { return false; }
            public void DeleteResource() { return; }
            public Stream OpenResourceForRead() { return new MemoryStream(file); }
            public Stream OpenResourceForWrite() { return null; }
        }

        #endregion

        #region Static Song Arrays

        /// <summary>
        /// Just the GM songs.
        /// </summary>
        public static SongFiles[] Songs_GM = new SongFiles[]
        {
            SongFiles.song_02,
            SongFiles.song_03,
            SongFiles.song_04,
            SongFiles.song_05,
            SongFiles.song_06,
            SongFiles.song_07,
            SongFiles.song_08,
            SongFiles.song_09,
            SongFiles.song_10,
            SongFiles.song_11,
            SongFiles.song_12,
            SongFiles.song_13,
            SongFiles.song_15,
            SongFiles.song_16,
            SongFiles.song_17,
            SongFiles.song_18,
            SongFiles.song_20,
            SongFiles.song_21,
            SongFiles.song_22,
            SongFiles.song_23,
            SongFiles.song_25,
            SongFiles.song_28,
            SongFiles.song_29,
            SongFiles.song_30,
            SongFiles.song_d1,
            SongFiles.song_d10,
            SongFiles.song_d2,
            SongFiles.song_d3,
            SongFiles.song_d4,
            SongFiles.song_d5,
            SongFiles.song_d6,
            SongFiles.song_d7,
            SongFiles.song_d8,
            SongFiles.song_d9,
            SongFiles.song_dungeon,
            SongFiles.song_dungeon5,
            SongFiles.song_dungeon6,
            SongFiles.song_dungeon7,
            SongFiles.song_dungeon8,
            SongFiles.song_dungeon9,
            SongFiles.song_folk1,
            SongFiles.song_folk2,
            SongFiles.song_folk3,
            SongFiles.song_gbad,
            SongFiles.song_gcurse,
            SongFiles.song_gday___d,
            SongFiles.song_gdngn10,
            SongFiles.song_gdngn11,
            SongFiles.song_gdungn4,
            SongFiles.song_gdungn9,
            SongFiles.song_geerie,
            SongFiles.song_ggood,
            SongFiles.song_gmage_3,
            SongFiles.song_gneut,
            SongFiles.song_gpalac,
            SongFiles.song_gruins,
            SongFiles.song_gshop,
            SongFiles.song_gsneak2,
            SongFiles.song_gsnow__b,
            SongFiles.song_gsunny2,
            SongFiles.song_magic_2,
            SongFiles.song_overcast,
            SongFiles.song_overlong,
            SongFiles.song_oversnow,
            SongFiles.song_raining,
            SongFiles.song_sneaking,
            SongFiles.song_sneakng2,
            SongFiles.song_snowing,
            SongFiles.song_square_2,
            SongFiles.song_sunnyday,
            SongFiles.song_swimming,
            SongFiles.song_tavern,
        };

        #endregion
    }
}
