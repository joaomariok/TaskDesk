# Reordering drags are the app's own, not Windows drag-and-drop

Windows drag-and-drop lifts a faded copy of the dragged row in place for about 60 ms before the copy follows the pointer. Its only custom image without that lift appears only over our own list, with a "Move" badge and a brief "not allowed" sign, and no source-side setting removes them. So dragging a Task or a List to reorder it captures the pointer, draws its own image, which can leave the window, and drops through the same gap logic.

## Consequences

Touch and pen start a drag by press-and-hold, edge scrolling is our own, and a row can't be dragged into another app.
