@echo off
rem Builds HdmiToggle.exe with the C# compiler that ships with Windows (.NET Framework 4.x)
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0HdmiToggle.exe" "%~dp0HdmiToggle.cs"
