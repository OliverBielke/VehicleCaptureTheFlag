using System.IO;
using PacMan.Game;
using UnityEngine;

namespace PacMan
{
    public class GameRecorder : MonoBehaviour
    {
        public string filename;
        private string _currentFile;

        public void StartRecording(PacManGameManager pacManGameManager)
        {
            if (enabled)
            {
                _currentFile = Application.streamingAssetsPath + "/Text/" + filename + "_" + System.DateTime.Now.ToLongTimeString().Replace(":", "_").Replace(" ", "") + ".pb";
                using var fileStream = new FileStream(_currentFile, FileMode.Create, FileAccess.Write, FileShare.Read);
            }
        }

        public void AppendState(PacManGameManager pacManGameManager)
        {
            if (enabled)
            {
                var writeState = GameStateParser.WriteState(pacManGameManager);
                using var fileStream = new FileStream(_currentFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                GameStateParser.WriteState(fileStream, writeState);
            }
        }
    }
}
