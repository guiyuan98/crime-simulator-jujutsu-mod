using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace CrimeJJK {
  public sealed class SkillAudio : IDisposable {
    readonly AudioClip[,] voices=new AudioClip[2,5],sounds=new AudioClip[2,4];
    readonly AudioSource voice;readonly AudioSource[] effects=new AudioSource[4];readonly float[] gains=new float[4];
    readonly List<AudioClip> owned=new List<AudioClip>();readonly GameObject root;
    public readonly int[,] VoiceStarts=new int[2,5],FxStarts=new int[2,4];
    public int Missing,Loaded;int cursor;public float VoiceGain=.85f,FxGain=.65f;
    public bool VoicePlaying {get{return voice && voice.isPlaying;}}
    public string VoiceId {get{return voice && voice.clip?voice.clip.name:"none";}}
    public float VoiceLevel {get{return voice?voice.volume:0;}}
    public string MixerName {get{return voice && voice.outputAudioMixerGroup?voice.outputAudioMixerGroup.name:"listener";}}
    public int PlayingEffects {get{int n=0;foreach(var s in effects)if(s && s.isPlaying)n++;return n;}}
    public SkillAudio(Transform parent) {
      root=new GameObject("JJK_skill_audio_bank");root.transform.SetParent(parent,false);
      voice=Source(root,"voice");
      for(int i=0;i<effects.Length;i++)effects[i]=Source(root,"sfx_"+i);
      for(int c=0;c<2;c++)for(int slot=0;slot<5;slot++)voices[c,slot]=Load("v"+c+slot);
      for(int c=0;c<2;c++)for(int slot=0;slot<4;slot++)if(c!=1 || slot!=2)sounds[c,slot]=Load("s"+c+slot);
    }
    static AudioSource Source(GameObject parent,string name) {
      var go=new GameObject("JJK_audio_"+name);go.transform.SetParent(parent.transform,false);var source=go.AddComponent<AudioSource>();source.playOnAwake=false;source.loop=false;source.spatialBlend=0;source.dopplerLevel=0;source.ignoreListenerPause=false;source.priority=name=="voice"?32:96;return source;
    }
    AudioClip Load(string id) {
      using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("jjk.audio."+id+".wav")) {
        if(stream==null){Missing++;return null;}
        using(var reader=new BinaryReader(stream)) {
          if(new string(reader.ReadChars(4))!="RIFF")throw new InvalidDataException("WAV RIFF missing: "+id);reader.ReadUInt32();if(new string(reader.ReadChars(4))!="WAVE")throw new InvalidDataException("WAV type: "+id);
          ushort format=0,channels=0,bits=0;int rate=0;byte[] bytes=null;
          while(stream.Position+8<=stream.Length){string tag=new string(reader.ReadChars(4));uint size=reader.ReadUInt32();long end=stream.Position+size;if(end>stream.Length)throw new InvalidDataException("WAV chunk: "+id);if(tag=="fmt "){format=reader.ReadUInt16();channels=reader.ReadUInt16();rate=reader.ReadInt32();reader.ReadUInt32();reader.ReadUInt16();bits=reader.ReadUInt16();}else if(tag=="data")bytes=reader.ReadBytes((int)size);stream.Position=end+(size%2);}
          if(format!=1 || channels!=1 || bits!=16 || rate!=44100 || bytes==null || bytes.Length<100)throw new InvalidDataException("Expected mono44100 PCM16: "+id);
          var samples=new float[bytes.Length/2];float peak=0;for(int i=0;i<samples.Length;i++){samples[i]=(short)(bytes[i*2]|bytes[i*2+1]<<8)/32768f;peak=Mathf.Max(peak,Mathf.Abs(samples[i]));}if(peak<.001f)throw new InvalidDataException("Silent audio: "+id);
          var clip=AudioClip.Create("JJK_"+id,samples.Length,1,rate,false);clip.SetData(samples,0);owned.Add(clip);Loaded++;return clip;
        }
      }
    }
    public void Cast(int character,int slot) {
      Say(character,slot);
      var clip=sounds[character,slot];if(!clip)return;
      if(character==0 && (slot==1 || slot==2))return;
      PlayEffect(clip,1,1);
      FxStarts[character,slot]++;
    }
    public void Release(int character,int slot){var clip=sounds[character,slot];if(clip){PlayEffect(clip,1,1);FxStarts[character,slot]++;}}
    public void PlayEffect(AudioClip clip,float gain,float pitch) {
      if(!clip)return;int index=cursor++%effects.Length;var source=effects[index];source.Stop();source.clip=clip;source.pitch=Mathf.Clamp(pitch,.2f,3);gains[index]=Mathf.Clamp01(gain);source.volume=FxGain*gains[index]*(VoicePlaying?.38f:1);source.Play();
    }
    public void Route(FPP_Player player){
      if(!player)return;
      foreach(var source in player.GetComponentsInChildren<AudioSource>(true))if(source && source.outputAudioMixerGroup){voice.outputAudioMixerGroup=source.outputAudioMixerGroup;foreach(var s in effects)s.outputAudioMixerGroup=source.outputAudioMixerGroup;break;}
    }
    public void Say(int character,int slot) {
      var clip=voices[character,slot];if(!clip)return;voice.Stop();voice.clip=clip;voice.pitch=1;voice.volume=VoiceGain;voice.Play();VoiceStarts[character,slot]++;
    }
    public void Tick(float voiceGain,float fxGain) {
      VoiceGain=Mathf.Clamp01(voiceGain);FxGain=Mathf.Clamp01(fxGain);voice.volume=VoiceGain;
      for(int i=0;i<effects.Length;i++)effects[i].volume=FxGain*gains[i]*(VoicePlaying?.38f:1);
    }
    public void Stop(){if(voice)voice.Stop();foreach(var s in effects)if(s)s.Stop();}
    public bool AllStopped {get{return !VoicePlaying && PlayingEffects==0;}}
    public void Dispose(){Stop();foreach(var clip in owned)Object.Destroy(clip);owned.Clear();if(root)Object.Destroy(root);}
  }
}
