//======= Copyright (c) Valve Corporation, All rights reserved. ===============
//
// Purpose: Notify developers when a new version of the plugin is available.
//
//=============================================================================

using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using System.IO;
using System.Text.RegularExpressions;
using System;

[InitializeOnLoad]
public class SteamVR_Update : EditorWindow
{
	const string currentVersion = "1.2.3";
	const string versionUrl = "https://media.steampowered.com/apps/steamvr/unitypluginversion.txt";
	const string notesUrl = "https://media.steampowered.com/apps/steamvr/unityplugin-v{0}.txt";
	const string pluginUrl = "https://u3d.as/content/valve-corporation/steam-vr-plugin";
	const string doNotShowKey = "SteamVR.DoNotShow.v{0}";

	static bool gotVersion = false;
	static UnityWebRequest wwwVersion, wwwNotes;
	static string version, notes;
	static SteamVR_Update window;

	static SteamVR_Update()
	{
		EditorApplication.update += Update;
	}

	static void Update()
	{
		if (!gotVersion)
		{
			if (wwwVersion == null)
				wwwVersion = StartRequest(versionUrl);

			if (!wwwVersion.isDone)
				return;

			if (UrlSuccess(wwwVersion))
				version = wwwVersion.downloadHandler.text;

			wwwVersion.Dispose();
			wwwVersion = null;
			gotVersion = true;

			if (ShouldDisplay())
			{
				var url = string.Format(notesUrl, version);
				wwwNotes = StartRequest(url);

				window = GetWindow<SteamVR_Update>(true);
				window.minSize = new Vector2(320, 440);
				//window.title = "SteamVR";
			}
		}

		if (wwwNotes != null)
		{
			if (!wwwNotes.isDone)
				return;

			if (UrlSuccess(wwwNotes))
				notes = wwwNotes.downloadHandler.text;

			wwwNotes.Dispose();
			wwwNotes = null;

			if (notes != "")
				window.Repaint();
		}

		EditorApplication.update -= Update;
	}

	static UnityWebRequest StartRequest(string url)
	{
		if (string.IsNullOrEmpty(url))
			return null;

		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
			url = "https://" + url.Substring("http://".Length);

		var request = UnityWebRequest.Get(url);
		try
		{
			request.SendWebRequest();
		}
		catch (InvalidOperationException)
		{
			request.Dispose();
			return null;
		}

		return request;
	}

	static bool UrlSuccess(UnityWebRequest www)
	{
		if (www == null)
			return false;

#if UNITY_2020_2_OR_NEWER
		if (www.result != UnityWebRequest.Result.Success)
			return false;
#else
		if (www.isHttpError || www.isNetworkError)
			return false;
#endif

		if (www.downloadHandler == null)
			return false;

		var text = www.downloadHandler.text;
		if (string.IsNullOrEmpty(text))
			return false;
		if (Regex.IsMatch(text, "404 not found", RegexOptions.IgnoreCase))
			return false;
		return true;
	}

	static bool ShouldDisplay()
	{
		if (string.IsNullOrEmpty(version))
			return false;
		if (version == currentVersion)
			return false;
		if (EditorPrefs.HasKey(string.Format(doNotShowKey, version)))
			return false;

		// parse to see if newer (e.g. 1.0.4 vs 1.0.3)
		var versionSplit = version.Split('.');
		var currentVersionSplit = currentVersion.Split('.');
		for (int i = 0; i < versionSplit.Length && i < currentVersionSplit.Length; i++)
		{
			int versionValue, currentVersionValue;
			if (int.TryParse(versionSplit[i], out versionValue) &&
				int.TryParse(currentVersionSplit[i], out currentVersionValue))
			{
				if (versionValue > currentVersionValue)
					return true;
				if (versionValue < currentVersionValue)
					return false;
			}
		}

		// same up to this point, now differentiate based on number of sub values (e.g. 1.0.4.1 vs 1.0.4)
		if (versionSplit.Length <= currentVersionSplit.Length)
			return false;

		return true;
	}

	Vector2 scrollPosition;
	bool toggleState;

	string GetResourcePath()
	{
		var ms = MonoScript.FromScriptableObject(this);
		var path = AssetDatabase.GetAssetPath(ms);
		path = Path.GetDirectoryName(path);
		return path.Substring(0, path.Length - "Editor".Length) + "Textures/";
	}

	public void OnGUI()
	{
		EditorGUILayout.HelpBox("A new version of the SteamVR plugin is available!", MessageType.Warning);

		var resourcePath = GetResourcePath();
		var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(resourcePath + "logo.png");
		var rect = GUILayoutUtility.GetRect(position.width, 150, GUI.skin.box);
		if (logo)
			GUI.DrawTexture(rect, logo, ScaleMode.ScaleToFit);

		scrollPosition = GUILayout.BeginScrollView(scrollPosition);

		GUILayout.Label("Current version: " + currentVersion);
		GUILayout.Label("New version: " + version);

		if (notes != "")
		{
			GUILayout.Label("Release notes:");
			EditorGUILayout.HelpBox(notes, MessageType.Info);
		}

		GUILayout.EndScrollView();

		GUILayout.FlexibleSpace();

		if (GUILayout.Button("Get Latest Version"))
		{
			Application.OpenURL(pluginUrl);
		}

		EditorGUI.BeginChangeCheck();
		var doNotShow = GUILayout.Toggle(toggleState, "Do not prompt for this version again.");
		if (EditorGUI.EndChangeCheck())
		{
			toggleState = doNotShow;
			var key = string.Format(doNotShowKey, version);
			if (doNotShow)
				EditorPrefs.SetBool(key, true);
			else
				EditorPrefs.DeleteKey(key);
		}
	}
}

