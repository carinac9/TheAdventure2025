using NAudio.Wave;
using System;

namespace TheAdventure.Audio
{
    public class SoundEffect : IDisposable
    {
        private readonly AudioFileReader _reader;
        private readonly WaveOutEvent _output;

        public SoundEffect(string filePath)
        {
            _reader = new AudioFileReader(filePath);
            _output = new WaveOutEvent();
            _output.Init(_reader);
        }

        public void Play()
        {
            _reader.Position = 0; 
            _output.Play();
        }

        public void Dispose()
        {
            _output.Dispose();
            _reader.Dispose();
        }
    }
}