# Personal Memoirs Reader v0.1.35 — Visual Studio source

1. Extract the versioned folder intact.
2. Open PersonalMemoirsReader.sln in Visual Studio 2022 17.8 or later with the .NET desktop development workload and .NET 8 SDK.
3. Right-click MainForm.cs in Solution Explorer and choose View Designer (Shift+F7). Edit controls using Properties and the Toolbox; save normally.
4. Press F5 to build and run. The F5-TTS button is a separate speech-engine action.

MainForm.Designer.cs holds the visible controls, layout and event connections. MainForm.cs holds form behavior; the other partial files contain engine and audio behavior. Runtime initialization runs on form load with a design-mode guard. No third-party designer controls are required. Python and model downloads are not needed to build the C# project or edit the form.

This is the current desktop source, based on v0.1.34, with Designer preparation in v0.1.35. F5 and Chatterbox generation still use the included Python workers and separately installed models. The verified native engine prototypes have NOT yet been integrated into this form. Voice quality and remaining app development are not claimed complete.

Validation: .NET 8 Release cross-build on Linux passed with zero warnings/errors. Visual Studio's interactive Windows Designer was not run in this Linux environment. Source compatibility was prepared but not interactively certified. No binaries, models, caches or Python environments are bundled.

All ZIP entries live under PersonalMemoirsReader_v0.1.35_VisualStudio/ to keep earlier versions separate. The running app's existing Documents and model-cache locations remain shared.
