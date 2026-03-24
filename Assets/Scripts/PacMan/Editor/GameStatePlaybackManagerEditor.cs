using System;
using System.IO;
using System.Linq;
using PacMan.Local;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PacMan.Editor
{
    [CustomEditor(typeof(GameStatePlaybackManager))]
    public class GameStatePlaybackManagerEditor : UnityEditor.Editor
    {
        private int _selectedIndex;
        private string[] _saveOptions = Array.Empty<string>();

        private void OnEnable()
        {
            RefreshSaveOptions();
            SyncSelectionWithCurrentValue();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            if (serializedObject.ApplyModifiedProperties())
            {
                SyncSelectionWithCurrentValue();
                MarkPlaybackManagerDirty((GameStatePlaybackManager)target);
            }

            GUILayout.Space(15f);

            if (_saveOptions.Length == 0)
            {
                RefreshSaveOptions();
                SyncSelectionWithCurrentValue();
            }

            if (_saveOptions.Length == 0)
            {
                EditorGUILayout.HelpBox("No replay save files (.pb) found in Assets/StreamingAssets/Text.", MessageType.Info);
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                var selectedIndex = EditorGUILayout.Popup("Replay save", Mathf.Clamp(_selectedIndex, 0, _saveOptions.Length - 1), _saveOptions);
                if (EditorGUI.EndChangeCheck())
                {
                    _selectedIndex = selectedIndex;
                    AssignSelectedSave((GameStatePlaybackManager)target);
                }
            }

            if (GUILayout.Button("Refresh saves"))
            {
                RefreshSaveOptions();
                SyncSelectionWithCurrentValue();
            }

            var playbackManager = (GameStatePlaybackManager)target;
            if (_saveOptions.Length > 0 &&
                !string.IsNullOrWhiteSpace(playbackManager.playbackFile) &&
                Array.IndexOf(_saveOptions, playbackManager.playbackFile) < 0)
            {
                EditorGUILayout.HelpBox(
                    $"Current save file '{playbackManager.playbackFile}' was not found in Assets/StreamingAssets/Text.",
                    MessageType.Warning);
            }
        }

        private void AssignSelectedSave(GameStatePlaybackManager playbackManager)
        {
            Undo.RecordObject(playbackManager, "Select replay save file");
            playbackManager.playbackFile = _saveOptions[_selectedIndex];
            SyncSelectionWithCurrentValue();
            MarkPlaybackManagerDirty(playbackManager);
        }

        private void RefreshSaveOptions()
        {
            var savesDirectoryPath = Path.Combine(Application.streamingAssetsPath, "Text");
            if (!Directory.Exists(savesDirectoryPath))
            {
                _saveOptions = Array.Empty<string>();
                _selectedIndex = 0;
                return;
            }

            var savesDirectory = new DirectoryInfo(savesDirectoryPath);
            _saveOptions = savesDirectory.GetFiles("*.pb")
                .Select(file => Path.GetFileNameWithoutExtension(file.Name))
                .OrderBy(fileName => fileName)
                .ToArray();
        }

        private void SyncSelectionWithCurrentValue()
        {
            if (_saveOptions.Length == 0)
            {
                _selectedIndex = 0;
                return;
            }

            var playbackManager = (GameStatePlaybackManager)target;
            var matchingIndex = Array.IndexOf(_saveOptions, playbackManager.playbackFile);
            _selectedIndex = matchingIndex >= 0 ? matchingIndex : 0;
        }

        private static void MarkPlaybackManagerDirty(GameStatePlaybackManager playbackManager)
        {
            EditorUtility.SetDirty(playbackManager);

            if (playbackManager.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(playbackManager.gameObject.scene);
            }
        }
    }
}
