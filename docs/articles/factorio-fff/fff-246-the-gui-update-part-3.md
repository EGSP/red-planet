# Friday Facts #246 - The GUI update (Part 3)

- Источник: https://factorio.com/blog/post/fff-246
- Авторы: kovarex, Twinsen, Albert
- Дата: 2018-06-08
- Темы: интерфейс
- Применимость к проекту: средняя
- Что извлечь: Порядок кнопок в диалогах, устройство окна загрузки и настроек генератора карты; довод о привычке пользователя против внутренней логики.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Ok→Cancel versus Cancel→Ok (kovarex)

A really strange debate started as a continuation of [FFF-238](https://www.factorio.com/blog/post/fff-238). I insisted that the button order should obviously always be OK Cancel, as in any UI I see around.

![](https://cdn.factorio.com/assets/img/blog/fff-246-windows-confirm-1.png)

![](https://cdn.factorio.com/assets/img/blog/fff-246-windows-confirm-2.png)

But little I knew, that this is actually specific to windows, and on Linux or macOS, the order is reversed:

![](https://cdn.factorio.com/assets/img/blog/fff-246-mac-confirm.png)

Eventually, we figured out, that [we are not the first one trying to solve the problem](https://www.nngroup.com/articles/ok-cancel-or-cancel-ok/).
The solution we are now experimenting it sounds like a bad idea: "Make it so much different and Factorio specific, that the way it is done in your specific system will not interfere with your muscle memory". Which brings me to the load game dialog mockup:

## Load game dialog (kovarex)

The load game dialog is currently the first dialog we are trying to implement with the new tileset and the mockup Albert created recently. The mockup looks like this:

[![](https://cdn.factorio.com/assets/img/blog/fff-246-load-game-dialog.png)](https://cdn.factorio.com/assets/img/blog/fff-246-load-game-dialog.png)

The idea is, that all the dialogs will have this flow from left to right, on the very left, you go back, on the very right, you continue to the next window. The same logic would be consistent in all the dialog windows. Options for example, have only the Back button in 0.16:

![](https://cdn.factorio.com/assets/img/blog/fff-246-only-back-in-options.png)

It is currently quite weird, as it is not clear whether it is confirm or cancel. Actually, if you press Escape you exit the options without saving it, but if you press "back" you confirm the changes.

So these will also have cancel on the left and confirm on the right. Escape is the same as clicking back.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-back-confirm-in-options.png)](https://cdn.factorio.com/assets/img/blog/fff-246-back-confirm-in-options.png)

Please note, that all of the pictures are work in progress.

## The map generator GUI (Twinsen)

Hopefully you are not getting tired of hearing about the GUI, because this game sure has a lot of them, and the next one to talk about is the Map Generator GUI.
Since we will have high-resolution spritesheets for the GUI, all the mockups below are done on 200% UI scale, so they are scaled down on this page. You can click each image to see it in full size.

You will notice things were moved around quite a bit. The seed and the replay toggle were moved to the top since they are not part of the preset. Then [the preset dropdown](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-dropdown.png), the reset to default button and the description, which are visible at all times so you know what you are editing. Everything inside the tabs is part of the preset.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-resources.png)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-resources.png)

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-terrain.png)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-terrain.png)

A new tab is the Enemies tab. The generation settings (Frequency, Size, Richness) are moved from being hidden in the terrain settings, to it's own category. Also the important Peaceful Mode is right at the top.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-enemy.png)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-enemy.png)

Finally the advanced tab contains the rest of the settings. Map width and height will now also be part of the preset.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-advanced.png)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-advanced.png)

The map exchange string can help you save all your settings in a string, but more importantly to share it with others. Clicking one of the buttons opens an in-window pop-up. The pop-up can be closed just by clicking outside. Player can use the "copy to clipboard" button, but can also copy directly from the textbox.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-export-string-popup-b.gif)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-export-string-popup-b.gif)

Now for the neat part: the map preview will be part of the map generator window. Clicking the "Preview" button expands the window (possibly with some quick animation). You can then change the settings as you like, then click Update to see the new map. You can have the map update automatically as you change the settings, by enabling the option. "Real-time update" is off by default since the preview generation can be a bit slow, and having the preview constantly flickering as you change the setting would be pretty annoying.

Notice that the "Preview" button has a warning icon next to it (in the images above). Hovering over it will give you a warning that the preview can spoil the exploration part of the game and should be used to understand the settings. My hope is that the majority of players will open the preview, play with the settings, then close the preview and re-roll the seed before pressing play.

[![](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-preview-02.png)](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-preview-02.png)

(click to view full size)

We plan that almost every widget in this GUI (and the of the game) will have tooltips briefly explaining what everything does, since for example not even us developers know what Enemy base Richness actually does any more.

As a bonus, [here is a mockup with all the ores showing their richness](https://cdn.factorio.com/assets/img/blog/fff-246-mapgen-preview-02-tooltips.png).

As always, let us know what you think on our [forum](https://forums.factorio.com/60870).
