---
name: TaskDesk
description: A quiet, native Windows 11 to-do list built on WinUI 3's Fluent controls.
colors:
  text-primary: "#000000E4"
  text-primary-dark: "#FFFFFF"
  text-secondary: "#0000009E"
  text-secondary-dark: "#FFFFFFC5"
  surface-base: "#F3F3F3"
  surface-base-dark: "#202020"
  surface-pane: "#EEEEEE"
  surface-pane-dark: "#1C1C1C"
  reminder-box: "#F9F9F9"
  reminder-box-dark: "#131313"
  card-fill: "#FFFFFFB3"
  card-fill-dark: "#FFFFFF0D"
  card-stroke: "#0000000F"
  card-stroke-dark: "#00000019"
  control-stroke: "#0000000F"
  control-stroke-dark: "#FFFFFF12"
  strong-stroke: "#00000072"
  strong-stroke-dark: "#FFFFFF8B"
  overdue: "#C42B1C"
  overdue-dark: "#FF99A4"
  caution: "#9D5D00"
  caution-dark: "#FCE100"
  caution-bg: "#FFF4CE"
  caution-bg-dark: "#433519"
  list-red: "#D13438"
  list-orange: "#F7630C"
  list-yellow: "#FFB900"
  list-green: "#107C10"
  list-blue: "#0078D4"
  list-purple: "#744DA9"
typography:
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "28px"
    fontWeight: 600
  subtitle:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "20px"
    fontWeight: 600
  body-strong:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "14px"
    fontWeight: 400
  caption:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "12px"
    fontWeight: 400
rounded:
  control: "4px"
  overlay: "8px"
  pill: "10px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
components:
  due-chip:
    textColor: "{colors.text-secondary}"
    typography: "{typography.caption}"
    rounded: "{rounded.pill}"
    padding: "1px 8px"
  due-chip-overdue:
    textColor: "{colors.overdue}"
  tag-chip:
    backgroundColor: "{colors.card-fill}"
    textColor: "{colors.text-primary}"
    typography: "{typography.caption}"
    rounded: "{rounded.pill}"
    padding: "1px 8px"
  reminder-box:
    backgroundColor: "{colors.reminder-box}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "4px"
  settings-card:
    backgroundColor: "{colors.card-fill}"
    rounded: "{rounded.control}"
    padding: "8px 16px"
    height: "60px"
  infobar-warning:
    backgroundColor: "{colors.caution-bg}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.control}"
    padding: "4px 4px 4px 12px"
    height: "48px"
  sidebar-badge-overdue:
    backgroundColor: "{colors.overdue}"
    typography: "{typography.caption}"
    rounded: "9px"
    height: "18px"
  rail-badge:
    backgroundColor: "{colors.text-secondary}"
    typography: "{typography.caption}"
    rounded: "{rounded.overlay}"
    height: "16px"
  dialog:
    padding: "24px"
    width: "440px"
  quick-add-card:
    backgroundColor: "{colors.card-fill}"
    rounded: "{rounded.overlay}"
    padding: "24px"
    width: "420px"
---

# Design System: TaskDesk

## Overview

**Creative North Star: "The Quiet Desk"**

TaskDesk is a tidy desk that is always there and never asks for attention. It is a native Windows 11 app on WinUI 3 and its Fluent controls, with Mica behind the window, so the window, the controls, the type ramp and every colour come from Windows itself and follow Light, Dark and high contrast without a line of TaskDesk code. The design's job is restraint: calm Fluent surfaces, one list the eye can scan in a glance, and colour that appears only where it carries meaning (a Task that is Overdue, a Due that is today, a List's own colour).

Its signature is soft and small, and it should be reused, not reinvented: pill chips for Due and Tags, count badges in the sidebar and the rail, a List-colour stripe on each row. These pieces are built on Fluent's own brushes and radii, so they read as part of Windows, but together they make a TaskDesk screen recognisable. Density is that of a desktop tool used all day at mid-size: 14 px body text, rows that fit one line, panels that appear on demand rather than crowding the list.

All colour goes through theme resources. The colour tokens in this file are the Fluent theme's Light and Dark values, recorded so tools can render them (8-digit values are CSS `#RRGGBBAA`; Fluent's XAML writes the same colours as `#AARRGGBB`); the app itself never hard-codes them (the only literal colours are the six List presets, which are user data).

**Key Characteristics:**
- Native Fluent first: every control is Fluent's own, restyled only through styles `BasedOn` the Fluent style.
- Meaning-only colour: Overdue, Today and List colour are the only hues beyond the system accent.
- A soft signature: pill chips, count badges, the List stripe.
- List first, details on demand: the detail pane opens when asked and stays closed when closed.
- Every look works in Light, Dark and high contrast, with meaning carried by text as well as colour.

## Colors

A neutral Fluent palette lifted by the user's own Windows accent, with two signal colours and six List colours.

### Primary
- **System Accent** (the Windows accent colour, no fixed value): checked Task checkboxes, the selected sidebar item, the insertion line that shows where a dragged List or Task lands, the primary dialog button (`AccentButtonStyle`) and "today" text and chip borders. The app reads it through Fluent (`AccentTextFillColorPrimaryBrush` for text, `AccentFillColorDefaultBrush` for the insertion line, Fluent's CheckBox and accent-button brushes elsewhere); it is never written as a hex value, so it always matches the user's Windows.

### Secondary
- **Overdue Red** (`overdue`, `overdue-dark`): Overdue Due chips and text, the Overdue sidebar badge, the High-priority flag, "Delete Task" and validation messages. It is Fluent's critical fill (`SystemFillColorCriticalBrush`), used through the app's `OverdueBrush`.

### Tertiary
- **Caution Amber** (`caution`, `caution-dark`, `caution-bg`, `caution-bg-dark`): the Medium-priority flag and warning InfoBars (amber icon on a pale amber or deep amber fill). Fluent's caution fill and caution background.

### Neutral
- **Ink** (`text-primary`, `text-primary-dark`): titles, field labels, body text.
- **Soft Ink** (`text-secondary`, `text-secondary-dark`): captions, counts, row captions, empty-state text, hints, the neutral Due chip, and the fill of the rail's count badges (with inverse text, at least 4.5:1 in Light and Dark).
- **Window Base** (`surface-base`, `surface-base-dark`): overlays such as the Tags flyout and the expanded sidebar.
- **Pane Grey** (`surface-pane`, `surface-pane-dark`): the detail pane's background, a step off the list.
- **Reminder Box** (`reminder-box`, `reminder-box-dark`): the detail pane's Reminders box, a step lighter than pane grey in Light (Fluent's `SolidBackgroundFillColorTertiary`) and a step darker in Dark (`ControlOnImageFillColorTertiary`), used through `ReminderBoxBrush`. Its outline is the control hairline (`ReminderBoxStrokeBrush`).
- **Card Veil** (`card-fill`, `card-fill-dark`) with **Hairline** (`card-stroke`, `card-stroke-dark`): Settings cards, the quick-add card and InfoBar edges.
- **Control Hairline** (`control-stroke`, `control-stroke-dark`): pill-chip borders and the Reminders box outline.
- **Strong Stroke** (`strong-stroke`, `strong-stroke-dark`): the hollow ring of a List with no colour (`ListStripeBrush`).

### List colours (data)
- **Red, Orange, Yellow, Green, Blue, Purple** (`list-red` … `list-purple`): the six presets a user can give a List, shown as the sidebar dot and the row stripe. A List with no colour shows a hollow ring and no stripe.

### Named Rules
**The Meaning-Only Rule.** Colour beyond the neutrals and the system accent marks a fact: Overdue, today, caution, or which List a Task belongs to. Never use colour for decoration.

**The Theme-Resource Rule.** Every colour in the app is a `{ThemeResource}` to a Fluent key or one of the meaning-named brushes (`OverdueBrush`, `TodayBrush`, `ListStripeBrush`, `ReminderBoxBrush`, `ReminderBoxStrokeBrush`); the only literal colours are the List presets. A new meaning gets a new named brush in each theme dictionary of `TaskDesk.Views/Styles/Foundation.xaml`, not a hex value in XAML.

**The Words-Too Rule.** In high contrast every signal colour becomes the system text colour, so meaning must also be in text: "Overdue" sits in the Overdue header and the badge shows a count.

## Typography

**Body Font:** Segoe UI (Windows' system font; the Fluent theme sets no font family of its own)
**Icon Font:** Segoe Fluent Icons (with Segoe MDL2 Assets)

**Character:** One family at five sizes, exactly Fluent's ramp; hierarchy comes from size and weight, never from a second typeface.

### Hierarchy
- **Title** (SemiBold 600, 28px): the Settings page title only.
- **Subtitle** (SemiBold 600, 20px): the page heading ("Today", a List's name, "#tag"), dialog headings, the detail pane's "Details".
- **Body Strong** (SemiBold 600, 14px): Settings group headings.
- **Body** (Regular 400, 14px): Task titles, field labels, sidebar items, dialog text, empty states.
- **Caption** (Regular 400, 12px): counts, chips, row captions, hints such as "Enter to add · Esc to cancel".

### Named Rules
**The Ramp-Only Rule.** Text uses one of the app's ramp styles (`TitleText`, `SubtitleText`, `BodyStrongText`, `BodyText`, `CaptionText`, `SecondaryCaptionText`, `FieldLabel`), each `BasedOn` Fluent's. No ad-hoc font sizes or weights.

**The One-Line Row Rule.** Row text never wraps: titles and captions are single-line with a trailing ellipsis, and the full title is in a tooltip. Fluent's text styles wrap by default, so rows set `NoWrap` explicitly.

## Layout

The main window has three areas: sidebar, list and detail pane, with 12 px gutters (`spacing.md`) that double as splitters. It starts at 900 × 600 and never goes below 800 × 500. The sidebar defaults to 180 px (140 to 400) and the detail pane to 320 px (320 to 600); both widths persist. The list keeps at least 240 px.

The layout responds to the width you actually use, mid-size and popped in and out:
- **Below 900 px**, the sidebar collapses to a 56 px icon rail. A hamburger button opens the full sidebar as a 260 px overlay.
- **Above 1100 px**, the detail pane docks in its own column. At 1100 px or less it overlays the right side of the list, at least 320 px wide.

Spacing sits on a 4 px rhythm: 4, 8, 12, 16 and 24 px. Fields in a form are stacked with their label above (4 px gap) and 12 px between fields. Dialogs, the quick-add card and the Settings page use 24 px outer padding; the detail pane uses 16 px.

**The Default-Size Rule.** Controls keep Fluent's default sizes: no padding, height or template-margin override unless this file names one. Controls side by side in a row then share one height (a button, a toggle, a text box and a drop-down are all 32 px); a command bar keeps its own default height.

**The Native-Metrics Rule.** TaskDesk content inside a Fluent control takes that control's own size and spacing from its theme resources, not hand-picked pixels: a List row in the rail draws its hover and selection background where a Smart View's highlight is (`NavigationViewItemButtonMargin` in from the item, `NavigationViewItemOnLeftMinHeight` tall), and its square is `NavigationViewItemOnLeftIconBoxHeight`, centred where the Smart View icons sit. It then lines up with the native items around it and follows Fluent when Fluent changes. What matches is the drawn background, not the row's box: a ListViewItem already draws its background 4 px in from its sides and 2 px in from its top and bottom, so a rail List row is a whole Smart View item, 48 × 40 with no margin, and the rail's Tag rows are 40 px tall the same way. The rail's Tags flyout is NavigationView's own flyout for a rail item's children (`ChildrenFlyout`): `NavigationViewItemChildrenMenuFlyoutPadding`, `OverlayCornerRadius`, rows of a Smart View item's height and pitch, opening at the rail's edge 4 px above the item the # button stands for (6 px above the button, which is that item's highlight).

**The On-Demand Rule.** The list owns the window. The detail pane opens on a click, Enter, F2 or a notification; it stays closed when the user closes it, and arrow keys only move the selection.

## Elevation & Depth

TaskDesk is flat at rest. Surfaces separate by tone and a 1 px hairline, never by a custom shadow: cards use the card veil with a hairline stroke, the detail pane sits on pane grey with a left-edge stroke, overlays use the window base with a hairline. Fluent's own transient surfaces (menus, flyouts, dialogs, the rail overlay) keep Fluent's shadows.

**The Fluent-Lift Rule.** New work may add lift, but only Fluent's own elevation, and only where it helps a surface read as in front, for example a card that responds to hover. Never invent a shadow value.

## Shapes

Corners follow Fluent: 4 px (`rounded.control`) on buttons, inputs, Settings cards and InfoBars, and 8 px (`rounded.overlay`) on overlays and the quick-add card. TaskDesk's own forms are round, and the Task checkbox is Fluent's, with its 4 px corners. Due and Tag chips are pills (10 px radius). Badges are full pills (18 px tall in the sidebar, 16 px on the rail). The List stripe is a 3 px bar with rounded ends, and the List dot is a 10 px circle or, for a List with no colour, a 10 px ring. On the rail a List is a 16 px square with 4 px corners, the size of a Smart View icon and centred where the icons sit: filled with its colour, or hollow in Strong Stroke for a List with no colour.

## Components

The signature pieces are listed first; reuse them before building anything new.

### Task Row
One line, read left to right: List stripe, the Subtasks chevron, checkbox, title, then repeat icon, Subtask progress ("2/5"), up to two Tag chips plus "+N", the List's name (only in Smart Views, Tag pages and searches), the Priority flag, and the Due chip at the far right. Right-hand items are 8 px apart. Rows take Fluent's list-item hover, selection and focus, like the sidebar, with 8 × 1 px padding so each row stays 26 px tall. A completed Task dims to 60 % with its title struck through, stays in place for 400 ms (no time when Windows animations are off), then moves into "Completed (N)" at once. The Completed Smart View shows its rows at full strength, with no dimming and no strike-through (the checked checkbox stays). Rows draw no border or background in any group.

The chevron is the 12 px glyph of the "Completed (N)" heading, in soft ink, in its own fixed column: pointing right while the Task's Subtasks are closed, down while they are open. A Task without Subtasks leaves the column blank, so every title starts in the same place. A click on the chevron opens or closes the Subtasks and nothing else. Screen readers hear the Task row as expanded or collapsed.

### Subtask Row
A Subtask's own row of the Task List, directly under its Task while the Task is open, in the Subtasks' manual order. No List stripe and no chevron: its checkbox sits in the column of the Task's title, then the Subtask's title in body text, and nothing on the right. It is as tall as a Task row (26 px) and takes the same Fluent hover, selection and focus. A ticked Subtask dims to 60 % with its title struck through and stays in place. Under a completed Task the block dims once: every Subtask row is at the Task's 60 %, ticked ones struck through. On the Completed Smart View its Subtask rows are at full strength with no strike-through, like its Task rows; the checked checkbox says a Subtask is ticked. Screen readers hear "Subtask", its title and whether it is checked.

### Task List
Each page is one list in one scroll, with no height cap on any group. On Today, an "Overdue (N)" heading comes first, in body-strong text in Overdue Red with the word "Overdue". The open Tasks follow with no heading and no gap. "Completed (N)" comes directly after the last open row: a quiet heading button with a chevron and body-strong text, collapsed by default. A click, Enter or Space on it expands or collapses the completed Tasks, which then show at full length in the same scroll; it keeps that state while the app runs, and screen readers hear its count and whether it is expanded. Headings scroll with their rows, never take the selection, and are not announced as Tasks; the arrow keys walk the rows across groups, Subtask rows included, and there is one selection across the list. The Completed Smart View is one plain list of completed Tasks with no headings.

An open Task reorders on a List's page by the app's own drag, not Windows drag-and-drop (ADR 0007): pressing its row with the mouse and moving past Windows' drag threshold starts it, and a press that does not get that far stays a click. Overdue, completed and Subtask rows never drag, and nothing drags in a Smart View, on a Tag page or in search results. A picture of the row follows the pointer, also outside the list and the window, with the point pressed staying under the pointer: the row as it looks in the list, on the theme's solid window background (`SolidBackgroundFillColorBaseBrush`), the whole picture at 80 %, with no shadow, border, badge or sign. For an open Task the picture is its block, the Task row and its shown Subtask rows; past 5 rows it shows the first 5, fading out over its bottom two fifths. The dragged rows stay as they are in the list and the selection never changes. Within about 40 px of the list's top or bottom edge the list scrolls, faster closer to the edge, and the insertion line keeps to the gap under the pointer. Releasing over the list drops the Task into the gap under the pointer; releasing outside it, Esc, the window losing focus, the pointer lost, the page changing or reloading, or the dragged Task leaving the list (deleted or completed) cancels: the picture and the line go at once and nothing moves. Another change, such as a Task added, leaves the drag on, and the drop uses the gap under the pointer at release.

While a Task is dragged to reorder it on a List's page, the sidebar's insertion line (2 px, rounded, `AccentFillColorDefaultBrush`) marks the gap it will land in: at the top of the row after the gap, or under the last open row. An open Task and its Subtask rows are one block, so the line never shows between a Task and its Subtasks or between two Subtasks; there it shows under the block's last Subtask row. The line overlays the rows without taking input, and hides on drop, when the drag ends or is cancelled, and when the drag leaves the list. Rows come, go and move without a fade: adding, deleting, completing, dropping or opening a Task changes only its own rows, at once, and every other row stays drawn.

### Task Checkbox
Fluent's own CheckBox, unstyled, with no label. In a row it drops the room Fluent reserves for a label (`MinWidth` and `MinHeight` 0), so the box takes 22 × 22 px and the row stays one compact line. Checked, it fills with the system accent. The detail pane uses the same box beside the Task's title.

### Due Chip
- **Style:** a borderline pill with no fill, caption text, 1 × 8 px padding.
- **States:** Overdue draws border and text in Overdue Red, Today in the accent text colour, and later dates in a hairline border with soft ink. Wording is relative: "Today 18:00", "Tomorrow", "Thu", "Thu 15 Oct", with the year only when it isn't the current one.

### Tag Chip
- **Style:** a pill with a card-veil fill, hairline border and ink caption text reading "#" plus the name ("#health"), at most 96 px of text before an ellipsis, 4 px apart.
- **Where:** the same chip on rows and in the detail pane. In the detail pane a quiet 16 px button with the Cancel glyph (U+E711) sits inside the chip and is the only way to remove the Tag; its accessible name is "Remove #health". In the Filter row a Tag is a Fluent toggle button reading "#health", like its neighbours "Has due" and "Has reminder".

### Sidebar
- **Style:** Windows Settings–style navigation on one shared background. Each item is an icon (Segoe Fluent Icons, 16 px), its name in body text, and its count in soft ink at the right; a count of zero is hidden. A List's icon is its dot or ring, and a Tag's is "#" in soft ink, so every name starts in the same column. Sections are Smart Views, which stay fixed at the top, then a divider and one scroll area holding Lists, "+ New List" directly under the last List, and Tags (hidden when there are none).
- **States:** the selected item takes Fluent's selected ListBox item style. Overdue's count is a solid Overdue Red badge with on-accent text. While a List is dragged to reorder it, a 2 px rounded line in the system accent (`AccentFillColorDefaultBrush`) marks the gap it will land in: at the top of the row after the gap, or under the last row. It overlays the rows without taking input, in the full pane and in the rail, and hides on drop, when the drag ends or is cancelled, and when the drag leaves the Lists.
- **Narrow:** the rail shows icons only, with soft-ink pill badges for counts (red for Overdue), each List as a 16 px rounded square of its colour (hollow without one) in a row the size of a Smart View item, with the name in a tooltip, and a "#" button opening a Tags flyout. Switching to or from the rail, or opening its overlay, swaps the Lists' rows without replaying their entrance; adding, deleting and reordering still animate. Badges are pills with a number and List squares carry no text, so a count never reads as a List.

### Page Heading
One heading row: on the left, the subtitle-size name of what is shown; a long name ends with an ellipsis instead of pushing the rest. At the row's right end, **Filter** sits directly left of the search box, 8 px apart: a `ToggleButton` at its default size (32 px, as tall as the search box) holding the Segoe Fluent Icons Filter glyph (U+E71C) with the label "Filter" on its right, pressed while the Filter row is shown. There is no command bar and no "⋯" overflow; Rename and Colour for a List live on the sidebar List's context menu. The row is only as tall as its controls (32 px), and every control is vertically centred. The search box names its scope ("Search in Work") and flexes between 160 and 280 px. The name fits first: search takes its 280 px, the name gets the rest, and a long name is trimmed down to 160 px before search shrinks; a name narrower than 160 px is never trimmed. Filter never shrinks. At 1100 px or less the detail pane overlays the page's right side; while it does, Filter and search are hidden (out of view, the tab order and UI Automation) and the row keeps its height, so quick add and the list stay put. Both come back when the pane closes or docks, and Ctrl+F closes the overlaid pane and focuses search. The Filter row (priority drop-down, "Has due", "Has reminder", Tag toggles and "Clear filters") sits under the heading row only when the Filter button opens it or while a Filter is on; it starts closed at every launch. Quick add follows directly, with a visible label naming what will happen to the new Task: "Add a task to Work" on a List's page, "Add a task to Inbox, due today" on Today, "Add a task to Inbox, tagged #work" on a Tag's page, and "Add a task to Inbox" on All. Upcoming, Overdue and Completed have no quick add, because a quick-added Task would not show there: the label and box are hidden and out of the tab order.

### Buttons
- **Shape:** Fluent's buttons, 4 px corners.
- **Primary:** Fluent's accent button, at most one per dialog, on the right.
- **Quiet:** transparent icon buttons (32 px square, Segoe Fluent Icons) for close and move actions; a transparent Overdue-red text button for "Delete Task".

### Cards / Containers
- **Settings card:** card veil, hairline border, 4 px corners, at least 60 px tall, 8 × 16 px padding. The title and a one-line description in soft ink sit on the left, and the control on the right. Cards sit 2 px apart under a body-strong group heading.
- **Reminders box:** in the detail pane, the Reminder rows sit in a box of reminder-box tone (a step lighter than pane grey in Light, a step darker in Dark) with a 1 px control hairline, 4 px corners and 4 px inner padding, so no row touches the outline. It is hidden while the Task has no Reminder; "Add reminder" sits below it, left-aligned with it. Each row is one line: a quiet full-width button holding the body-text wording (8 × 4 px padding), then a quiet × button. The text takes the width the × leaves and ends in "…" when it does not fit, so it never runs under the × or past the box; at the 320 px pane "1 day before · Sun 25 Oct 09:00" fits whole. The row reads "Today 09:00", "Tomorrow 09:00" or the date ("Sun 25 Oct 09:00", with the year when not the current one), never a bare weekday; a before-Due row leads with its compact offset ("15 min", "2 h", "1 day", "2 weeks") and " before · ". Its tooltip and accessible name keep the full wording ("1 day before Due, Sun 25 Oct 09:00"). A past row looks like a completed Task: dimmed to 60 % and struck through, with no "past" word; screen readers still hear ", past".
- **Quick-add card:** a 420 px borderless window drawn as a card: card veil, hairline, 8 px corners, 24 px padding. It holds a 20 px "New Task" title box, then List and Due side by side, then the caption hint.

### Inputs / Fields
- **Style:** Fluent's text boxes, combo boxes and date pickers, unrestyled, each with a visible label above (`FieldLabel`) and an accessible name.
- **Focus:** Fluent's focus visual. An empty list takes no keyboard focus, so no outline is drawn around nothing.
- **Time:** the native time picker (minute steps of 1, the system clock), with a quiet clear button beside it where the time is optional (Due time, the Reminder flyout's time). A Reminder saved with a date only fires at the default Reminder time, which its picker shows when the flyout is reopened.
- **Errors:** a validation error shows in Overdue Red caption text directly under its field; an error that belongs to no field (a failed save) shows under the detail pane's fixed header, which a hairline parts from the scrolling fields.
- **Read-only facts:** shown as text, never as a read-only box (the detail pane's List is its dot or ring and name).

### InfoBar
- **Style:** a 48 px bar with 4 px corners and a hairline border, holding an icon, a message, an optional action button and an optional close button (32 px).
- **Severities:** Informational (attention fill, accent info icon) for "Task deleted · Undo", which lasts 10 seconds. Warning (caution fill, amber icon) for the notifications fallback and the hotkey conflict.

### Dialog
One shared Fluent dialog, 440 px wide with 24 px padding: a subtitle heading, body text, an optional labelled input, then buttons on the right. The order is other choices, then Cancel, then the accent primary. Esc always cancels. For destructive actions (deleting a List or Tag, replacing all data) Cancel is the default and holds focus, so Enter never destroys.

### Empty State
Plain soft-ink body text with no box or outline, specific to the page ("Nothing due today.", "No Tasks in Work yet. Type above to add one."). A search with no match adds a "Clear search" button.

## Do's and Don'ts

### Do:
- **Do** use the native WinUI control first; a derived control gets its own style `BasedOn` the Fluent default style, in `Styles/Foundation.xaml`.
- **Do** take colour from Fluent keys or the meaning-named brushes through `{ThemeResource}`, and add any new meaning to each theme dictionary of `Styles/Foundation.xaml`.
- **Do** reuse the signature pieces (pill chips, badges, List stripe) before designing a new one.
- **Do** give every control a visible label and an `AutomationProperties.Name`, and check it in Light, Dark and high contrast.
- **Do** keep rows to one line, with the full title in a tooltip.
- **Do** let Windows' animation setting switch motion off; the 400 ms completion hold drops to zero.

### Don't:
- **Don't** hard-code a colour in XAML; only the six List presets are literal. `Transparent` is not a colour and is allowed.
- **Don't** use spacing resources of type `Double` for `Margin` or `Padding`; write `Thickness` values.
- **Don't** add a second typeface or sizes outside the Fluent ramp.
- **Don't** invent shadow values; use Fluent's own elevation or none.
- **Don't** put more than one accent button in a dialog, or make a destructive action the Enter default.
- **Don't** reopen a pane, panel or overlay the user closed without an explicit request.
- **Don't** follow web conventions where Windows has its own (window chrome, dialogs, file pickers, notifications).
