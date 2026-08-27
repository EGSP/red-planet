# Friday Facts #279 - Train GUI & Modern Spitter

- Источник: https://factorio.com/blog/post/fff-279
- Авторы: kovarex, Albert, Ernestas, V453000
- Дата: 2019-01-25
- Темы: интерфейс, UX
- Применимость к проекту: средняя
- Что извлечь: Интерфейс управления поездами: перетаскивание, наглядное представление условий ожидания.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello,

# FFF motivation (kovarex)

This weeks Friday Facts are a good example of how they serve not only the obvious role of*informing the community*, but also helps us to divide the work into smaller chunks with individual deadlines. As this Friday Facts was scheduled to be about the Train GUI, we had to put special effort into finishing everything planned for the Train GUI, so it can be presented, and we can move to another task and 0.17 comes in reasonable time :).

# The train GUI (kovarex)

We introduced the plan and mock-ups for the Train GUI quite some time ago in [FFF-212](https://www.factorio.com/blog/post/fff-212). Many things changed since then, the final GUI style and rules are different now, and we also re-iterated the UX of it. But the main general idea of the layout remained.

The train schedule is one big list with stations and conditions all visible, so you don't have to click through the list-box of individual stations to see the conditions.

![](https://cdn.factorio.com/assets/img/blog/fff-279-train-gui-1.png)

### Drag and drop

Previously, stations and conditions were added after the currently selected station/condition. Since there is no selection now, you just always add to the end. Changing station/condition order is done by drag and drop. The drag and drop was possible in the list-box version as well, but it was quite a hidden feature, as it was the only list-box in the whole game that allowed that, and there was no visual indication of that functionality.

We also believe, that the visual feedback of re-arranging is now better than before.

[видео: fff-279-drag-drop-017.webm](https://cdn.factorio.com/assets/img/blog/fff-279-drag-drop-017.webm)
[видео: fff-279-drag-drop-017.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-drag-drop-017.mp4)

### Wait condition visualisation

One of the things I always missed in the Train GUI was an indication of how much longer the train going to wait in the station. After some discussions, we decided to give visual feedback to all reasonable conditions by gradually changing the style of the condition frame to green as if it was a progress bar.

The question is, how do we calculate the fraction of completeness for individual type of wait conditions?

- **Elapsed time** - The most obvious one, as it is known how much time the train has spent in the station, and how much more is remaining.

- **Inactivity time** - Also simple, similar to elapsed time.

- **Inventory full** - We can calculate the fullness of individual cargo wagons and average it.

- **Inventory empty** - Simply (1 - fraction of full) :)

- **Item count** - Here it starts to be tricky. If the comparator is > (greater than) or ≥(greater or equal than), we just calculate the amount of that item and divide it by the goal. If the comparator is anything else, I can't show a progress. I know how far I'm from the goal, but I don't know what to compare it to, so in this case, we just show either not completed at all, or fully completed.

- **Fluid condition** - Basically the same as Item count. **Circuit condition** - Here we just show nothing or full, as we can't really know. The result is quite nice for simple cases: [видео: fff-279-full-empty-loop.webm](https://cdn.factorio.com/assets/img/blog/fff-279-full-empty-loop.webm) [видео: fff-279-full-empty-loop.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-full-empty-loop.mp4) And in more complicated cases, it can be a great tool to quickly identify a problem: [видео: fff-279-train-complex-conditions.webm](https://cdn.factorio.com/assets/img/blog/fff-279-train-complex-conditions.webm) [видео: fff-279-train-complex-conditions.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-train-complex-conditions.mp4) ### Temporary stations The second most desired feature is temporary stations. The main motivation for them is the usage of trains for personal transportation. When I want to do that now, I have to: Open the map and search where I want to go, and find the nearest station.

- Remember the station name.

- Enter the locomotive and add that station (by searching through the list) to the schedule.

- Confirm some random wait condition.

- Press the "go to the station" button.

- When it gets there, delete the station from the schedule again.

With temporary station support, what I do is:

- Enter the locomotive and find the place where I want to go directly in the map preview.

- Ctrl+Click nearest rail/station.

Using the control click will select the nearest station or even just a rail, and set it as a target of temporary station. By this action, the temporary station is added in front of the current goal (if there is any) and is ordered to go there. Once the goal is reached, the station is automatically removed from the schedule and the train will continue to the goal it was headed to before adding the temporary station.

This means, that I can carelessly hijack any train passing by for my personal travel, as it will continue its schedule once I'm done with it. It also adds some possibilities for when I need to send train from dried out mines into smelting and depots etc.

# The modern Spitter (Ernestas, Albert)

In the animation below you can see the new high-res spitter walking in 3 levels. As you can see we took the chance to work a bit different the same concept as before but integrating some new details that in my opinion makes them more interesting.

![](https://cdn.factorio.com/assets/img/blog/fff-279-spitter-walk.gif)

*Note: the colors are not necessary final.*

We keep the modular shell due the familiarity with the biters. Also for convenience with the color tint. The same way [the modern Biter](https://www.factorio.com/blog/post/fff-268) moves a bit closer to a spider concept, the Spitters are going more to a [woodlouse](https://en.wikipedia.org/wiki/Woodlouse) or a small centipede. The point is it's flexibility at the time of shooting, also they shoot from far away from the target, so there's no need to look specially strong or agile.

![](https://cdn.factorio.com/assets/img/blog/fff-279-spitter-shoot.gif)

The 'classic' design of the Spitters is based in the idea that they shoot from some height in order to bypass the walls of your factory with their acid spits. This is a nice concept, but at the time of walking it doesn't make much sense to keep standing up. That's why we took the chance of improving this feature, and we divided the modes of the Spitter in two: walking and shooting.
Now when shooting, they stand up as before, but for walking they use all the legs.

We also added this big mouth claws that open and close when spitting to give a better expression. The rest is pure fun. We have also been with Vaclav on the way they shoot...

# Worm and Spitter stream attack (V453000)

With the new high resolution Worms (and now also Spitters), their projectiles started to look even more out of place than before. On top of that, a homing acid projectile makes about as much sense as a homing laser beam. We were quite sure we want Worms and Spitters to spit acid, but closer to how the flamethrower 'stream attack' behaves, so we started with that.

[видео: fff-279-no-prediction.webm](https://cdn.factorio.com/assets/img/blog/fff-279-no-prediction.webm)
[видео: fff-279-no-prediction.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-no-prediction.mp4)

While visually it makes much better sense and the acid looks much nicer thanks to the work of Dominik (from the GFX gang, not pipe programmer gang) and Ernestas, the acid stream has a downside - it can easily be dodged. In fact, as long as you keep moving, the stream never hits you. Therefore we added predictive targeting to streams - so Worms, Spitters and Flamethrower turrets can hit the target unless it changes direction.
It is still possible for the player to dodge them if they try, but with higher numbers of Worms it becomes a lot harder.

[видео: fff-279-prediction.webm](https://cdn.factorio.com/assets/img/blog/fff-279-prediction.webm)
[видео: fff-279-prediction.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-prediction.mp4)

When the target too fast and/or the predicted position is out of range, the prediction is turned off. We will probably tweak this so that the prediction tries to go as close to the border of its range as possible. At some point even the homing projectiles from 0.16 miss so this is not much difference, the threshold probably comes a little earlier though.

Where the acid stream hits, it creates an acid splash on the ground. The acid splash does damage to trespassers (currently not buildings), and applies a slow debuff. Enemies are not affected by the acid splash.

To make the effect less binary (slow/fast), we added a tapering slow - at first it applies a lot of slowdown, but the effect linearly decreases over a few seconds until it disappears completely.

All of the worms have a large enough stream attack that it damages more than one building if they are next to each other. Spitters smaller than behemoth do not possess this ability.

[видео: fff-279-super-trooper.webm](https://cdn.factorio.com/assets/img/blog/fff-279-super-trooper.webm)
[видео: fff-279-super-trooper.mp4](https://cdn.factorio.com/assets/img/blog/fff-279-super-trooper.mp4)

Some details and numbers are still being worked on, but with all of the changes combined, it should be more fun to attack biter bases. If you are not a fan of 'combat dancing', you can always just invest a bit more into military technologies and win with brute force.

As always, let us know what you think on our [forum](https://forums.factorio.com/64582).
