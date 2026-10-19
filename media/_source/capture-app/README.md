# Capture switch for the app prints (scratch copy only)

Not compiled into the app. To regenerate `media/_source/ui/`:

1. Copy the repo to a scratch folder (without `bin`, `obj` or `.git`).
2. Save `CaptureRunner.cs.txt` there as `PacConsole/Capture/CaptureRunner.cs`.
3. In `App.xaml.cs`, after `InitializeNavigationAsync(...)`, add:

   ```csharp
   var argv = Environment.GetCommandLineArgs();
   var at = Array.IndexOf(argv, "--capture");
   if (at >= 0 && at + 1 < argv.Length) _ = Capture.CaptureRunner.RunAsync(MainWindow, argv[at + 1]);
   ```

4. Build Release and run `PacConsole.exe --capture <repo>/media/_source/ui`. The app walks the operator story, saves the PNGs and `capture.log`, then closes itself (about 2.5 min).
