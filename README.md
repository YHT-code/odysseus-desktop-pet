# Odysseus desktop pet — prototype

A chunky pixel-art Odysseus with long silver-white hair who returns from his voyage to keep you company at the bottom-right of your desktop.

## Try him

Open `Odysseus\Start Odysseus.lnk`. The pet rests on the top edge of the primary monitor's taskbar and performs a backflip when launched.

- Use the compact pill switch at the bottom-right of the Windows taskbar to show or hide Odysseus. Its black thumb slides between sides: fully visible on the right while he is shown, and dimly visible on the left while he is hidden.
- Hover over Odysseus to reveal two buttons. `EMOTE` opens character actions; `TOOLS` opens the rounded Voyage Desk for to-do and notes.
- Click the character to give him a head pat.
- Drag him around with the mouse; release to drop him back down to the taskbar.
- Double-click him to replay the backflip.
- Move your pointer nearby and he tracks it with 16-direction gaze.
- Right-click him for taskbar patrol, snacks, greetings, heroic stumbles, pondering strategy, naps, pet size, quiet mode, startup settings, and Quit.
- `Nap / wake` lets Odysseus grow drowsy and crouch through several closed-eye frames before he lowers into a horizontal side-sleeping pose on the taskbar. He breathes gently while `Z`, `ZZ`, `ZZZ` symbols rise above him. Click him to lift the sleeping pose and reverse the crouch sequence back to standing.
- The system tray icon remains available as a backup for actions or fully exiting both Odysseus and the taskbar switch.

`Start with Windows` means launch after you sign in to your Windows account. A graphical desktop pet cannot appear before Windows signs you in. Startup uses only your account's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\OdysseusDesktopPet` entry. Turn it off from the pet's right-click menu. Quitting closes the current session without changing your startup preference.

Keep the `Odysseus` folder in its current location while startup is enabled. If you move it, run `Install.ps1` from the new project location to update startup.

## Build and install

The app uses the Microsoft .NET 10 Windows Desktop Runtime already installed on this PC. Its artwork is local; interactions do not use a network service, microphone, or account.

- `build.ps1` builds the WPF app and creates its launch shortcut. Building requires the .NET 10 SDK.
- `Install.ps1` enables startup for the account that runs it, then launches the pet.
- `Disable startup.ps1` removes only this pet's startup entry.
- `src\Odysseus.cs` contains the app, animation playback, and interactions.
- `art\odysseus-pixel` contains the current hatch-pet artwork and validation results. The earlier smooth-cartoon draft remains in `art\odysseus` as a backup.
- `qa` contains application verification artifacts.

Preferences and error logs live under `%LOCALAPPDATA%\OdysseusDesktop`. Only quiet mode and size are saved; each launch begins awake in the bottom-right corner. One instance runs per Windows session.

This interactive prototype includes drag & drop, taskbar patrol, snack, greeting, stumble, and pondering animations. There is no conversational AI, persistent hunger system, or audio.

## Voyage Desk

Open `TOOLS` from the hover buttons. The compact Voyage Desk now contains only `To-Do` and `Notes`; calendar syncing and calendar reminders have been removed.

All custom surfaces use the same warm pixel-art treatment as Odysseus: softly rounded controls, warm block colors, subtle shadows, and crisp lightly pixelated text. This includes the tools panel, tabs, inputs, buttons, scrollbar, speech bubble, hover controls, menus, and taskbar switch.

The compact heading-free tools panel grows from its bottom-right trigger anchor while fading in, rising 20 pixels, and settling with a gentle overshoot. Select `TOOLS` again, press Escape, or click elsewhere to dismiss it with a quicker fade, slight shrink, and downward drift.

Switching between the two pages moves both pages together: the old page leaves toward the previous tab while the new page arrives from the next tab, with fade and subtle scale depth. The active pill indicator slides and gently settles under the selected tab.

The `To-Do` tab stores a persistent checklist. Add tasks with the button or Enter, click task text to toggle its strikethrough, and remove it with the dim `×`. The `Notes` tab stores free-form notes locally and saves them when you select `Save note` or close the window. Both stay under `%LOCALAPPDATA%\OdysseusDesktop` on this PC.
