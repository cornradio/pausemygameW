; PauseManager.ahk - Suspend/Resume active process with stack and tray menu
; Requirements: AutoHotkey v1.1+ and Sysinternals PsSuspend (pssuspend.exe) in the same folder or in PATH
; Hotkeys:
;   Ctrl+Alt+P => Suspend foreground process (unless whitelisted). Push to pausedexe.txt
;   Ctrl+Alt+R => Resume last suspended process (LIFO from pausedexe.txt)
; Tray menu:
;   Right-click tray icon to select any paused process to resume

#NoEnv
#SingleInstance, Force
#Persistent
SetBatchLines, -1
SetWorkingDir, %A_ScriptDir%

; ---------- Config ----------
PausedListFile := A_ScriptDir . "\\pausedexe.txt"
PsSuspendPath := A_ScriptDir . "\\pssuspend.exe"
; If not found locally, rely on PATH
if !FileExist(PsSuspendPath)
	PsSuspendPath := "pssuspend.exe"

; Whitelist: processes that should never be suspended
; Add names in lowercase
whiteList := {}
list := "
(
explorer.exe
searchui.exe
sihost.exe
shellexperiencehost.exe
applicationframehost.exe
dwm.exe
taskmgr.exe
systemsettings.exe
systemsettingsbroker.exe
startmenuexperiencehost.exe
)"
Loop, Parse, list, `n, `r
{
	name := A_LoopField
	if (name = "")
		continue
	StringLower, name, name
	whiteList[name] := true
}

; Ensure paused list file exists
if !FileExist(PausedListFile)
	FileAppend,, %PausedListFile%

; Build initial tray
Menu, Tray, NoStandard
Menu, Tray, Add, Resume Last (Ctrl+Alt+R), Tray_ResumeLast
Menu, Tray, Add
Menu, Tray, Add, Resume Specific..., Tray_ShowResumeMenu
Menu, Tray, Add
Menu, Tray, Add, Open pausedexe.txt, Tray_OpenFile
Menu, Tray, Add, Clear list, Tray_Clear
Menu, Tray, Add
Menu, Tray, Add, Download PsSuspend..., Tray_DownloadPsSuspend
Menu, Tray, Add, Exit, Tray_Exit
Menu, Tray, Tip, PauseManager
Menu, Tray, Icon, %A_WinDir%\system32\shell32.dll, 47
; Ensure the submenu exists before first use
Menu, ResumeMenu, Add, (none), Tray_NoOp
return

; ---------- Hotkeys ----------
^!p::
	SuspendActive()
return

^!r::
	ResumeLast()
return

; ---------- Core Functions ----------
EnsurePsSuspendAvailable() {
	global PsSuspendPath
	localPath := A_ScriptDir . "\\pssuspend.exe"
	if !FileExist(localPath) {
		Run, https://learn.microsoft.com/zh-tw/sysinternals/downloads/pssuspend
		return false
	}
	return true
}

SuspendActive() {
	global PausedListFile, PsSuspendPath, whiteList
	if (!EnsurePsSuspendAvailable())
		return
	WinGet, pid, PID, A
	if !pid {
		ShowOSD("No active window.", 1000, "DDDDDD")
		return
	}
	; Get process name and active window handle
	WinGet, procName, ProcessName, A
	WinGet, hWnd, ID, A
	; Minimize the active window before suspend
	WinMinimize, ahk_id %hWnd%
	procNameLower := procName
	StringLower, procNameLower, procNameLower
	if (whiteList.HasKey(procNameLower)) {
		ShowOSD("Skipped (whitelisted): " . procNameLower, 1200, "BBBBBB")
		return
	}
	; Suspend using pssuspend (legacy-compatible)
	RunWait, %ComSpec% /c ""%PsSuspendPath%" %pid%" , , Hide UseErrorLevel
	if (ErrorLevel) {
		ShowOSD("Failed to suspend PID " . pid . " (" . procName . ").", 1500, "FF5252")
		return
	}
	; Record entry as: pid|procName|timestamp|hWnd
	entry := pid . "|" . procName . "|" . A_Now . "|" . hWnd
	FileAppend, %entry%`n, %PausedListFile%
	ShowOSD("Suspended " . procName . " (PID " . pid . ").", 1200, "FFC107")
	; Refresh tray submenu
	BuildResumeMenu()
}

ResumeLast() {
	global PausedListFile
	lines := ReadAllLines(PausedListFile)
	if (lines.MaxIndex() < 1) {
		ShowOSD("No paused processes.", 1000, "DDDDDD")
		return
	}
	last := lines.Pop()
	if ResumeEntry(last) {
		; Write back remaining lines
		WriteAllLines(PausedListFile, lines)
		BuildResumeMenu()
	}
}

ResumeEntry(entry) {
	global PsSuspendPath
	if (!EnsurePsSuspendAvailable())
		return false
	StringSplit, parts, entry, |
	pid := parts1
	proc := parts2
	hWnd := parts4
	if !pid {
		return false
	}
	; Resume using legacy syntax for compatibility
	RunWait, %ComSpec% /c ""%PsSuspendPath%"  -r %pid%" , , Hide UseErrorLevel
	if (ErrorLevel) {
		ShowOSD("Failed to resume PID " . pid . " (" . proc . ").", 1500, "FF5252")
		return false
	}
	; Try to show and activate the window
	if (hWnd) {
		WinShow, ahk_id %hWnd%
		WinRestore, ahk_id %hWnd%
		WinSet, Transparent, OFF, ahk_id %hWnd%
		WinActivate, ahk_id %hWnd%
	} else {
		WinGet, winList, List, ahk_pid %pid%
		if (winList >= 1) {
			first := winList1
			WinShow, ahk_id %first%
			WinRestore, ahk_id %first%
			WinSet, Transparent, OFF, ahk_id %first%
			WinActivate, ahk_id %first%
		}
	}
	ShowOSD("Resumed " . proc . " (PID " . pid . ").", 1200, "4CAF50")
	return true
}

BuildResumeMenu() {
	global PausedListFile, ResumeMap
	Menu, ResumeMenu, DeleteAll
	lines := ReadAllLines(PausedListFile)
	ResumeMap := {}
	if (lines.MaxIndex() >= 1) {
		Loop % lines.MaxIndex()
		{
			idx := lines.MaxIndex() - A_Index + 1
			ln := lines[idx]
			StringSplit, p, ln, |
			pid := p1
			proc := p2
			ts := p3
			label := proc " (PID " pid ") - " FormatTimeSafe(ts)
			Menu, ResumeMenu, Add, %label%, Tray_ResumeSpecific
			ResumeMap[label] := ln
		}
	} else {
		Menu, ResumeMenu, Add, (none), Tray_NoOp
	}
}

FormatTimeSafe(ts) {
	FormatTime, out, %ts%, yyyy-MM-dd HH:mm:ss
	return out
}

ReadAllLines(path) {
	arr := []
	if !FileExist(path)
		return arr
	FileRead, content, %path%
	Loop, Parse, content, `n, `r
	{
		line := A_LoopField
		if (line = "")
			continue
		arr.Push(line)
	}
	return arr
}

WriteAllLines(path, arr) {
	; Overwrite file with provided lines
	fileContent := ""
	for idx, ln in arr
		fileContent .= ln . "`r`n"
	FileDelete, %path%
	FileAppend, %fileContent%, %path%
}

; ---------- Tray Handlers ----------
Tray_ResumeLast:
	ResumeLast()
return

Tray_ShowResumeMenu:
	; Rebuild to reflect latest
	BuildResumeMenu()
	Menu, ResumeMenu, Show
return

Tray_ResumeSpecific:
	global ResumeMap, PausedListFile
	; Identify which item clicked by A_ThisMenuItem
	label := A_ThisMenuItem
	entry := ResumeMap[label]
	if (entry = "")
		return
	if ResumeEntry(entry) {
		; Remove this specific entry from file
		lines := ReadAllLines(PausedListFile)
		for i, ln in lines {
			if (ln = entry) {
				lines.RemoveAt(i)
				break
			}
		}
		WriteAllLines(PausedListFile, lines)
	}
return

Tray_OpenFile:
	Run, notepad.exe "%PausedListFile%"
return

Tray_Clear:
	FileDelete, %PausedListFile%
	FileAppend,, %PausedListFile%
	BuildResumeMenu()
return

Tray_NoOp:
return

Tray_Exit:
	ExitApp
return

Tray_DownloadPsSuspend:
	Run, https://learn.microsoft.com/zh-tw/sysinternals/downloads/pssuspend
	ShowOSD("download pssuspend.exe ", 2600, "84C1FF")
return

; Initialize supporting global map for resume entries
ResumeMap := {}
; Build initial resume menu
BuildResumeMenu()

; ---------- Notification OSD ----------
ShowOSD(text, duration := 1200, textColor := "FFFFFF", bgColor := "101010", alpha := 200) {
	; Create a dedicated GUI for the OSD
	Gui, OSD:New, +AlwaysOnTop -Caption +ToolWindow +LastFound +HwndOSDhwnd
	Gui, OSD:Color, %bgColor%
	Gui, OSD:Font, s16 Bold, Segoe UI
	winW := 800
	winH := 30
	x := (A_ScreenWidth - winW) / 2 
	y := 60
	; ; Shadow layer
	; Gui, OSD:Add, Text, x6 y6 w%winW% h%winH% c000000 Center, %text%
	; Foreground text
	Gui, OSD:Add, Text, x0 y0 w%winW% h%winH% c%textColor% Center, %text%
	Gui, OSD:Show, NA x%x% y%y% w%winW% h%winH%
	; Fade in on OSD only
	step := 25
	trans := 0
	while (trans < alpha) {
		trans += step
		if (trans > alpha)
			trans := alpha
		WinSet, Transparent, %trans%, ahk_id %OSDhwnd%
		Sleep, 10
	}
	; Hold
	Sleep, %duration%
	; Fade out on OSD only
	while (trans > 0) {
		trans -= step
		if (trans < 0)
			trans := 0
		WinSet, Transparent, %trans%, ahk_id %OSDhwnd%
		Sleep, 10
	}
	Gui, OSD:Destroy
}
