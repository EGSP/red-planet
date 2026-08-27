# Friday Facts #296 - All kinds of bugs

- Источник: https://factorio.com/blog/post/fff-296
- Авторы: Klonan, Rseding
- Дата: 2019-05-24
- Темы: производительность, отладка, процесс
- Применимость к проекту: высокая
- Что извлечь: Правило: причина потери производительности почти никогда не там, где ожидается, поэтому измерение предшествует правке. Отдельно — довод против проверки на null вместо поиска причины.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## Cars/Tanks remember their color Klonan

This really is a tiny feature, the car and tank will now save the color of the passengers when they exit the vehicle.

![](https://cdn.factorio.com/assets/img/blog/fff-296-car-color.png)

So now you won't forget which vehicle you were driving, and can warn everyone else on the server: "Pink tank is mine".

## All kinds of bugs Rseding

As uninteresting as it is; most bugs are boring and typically involve missing code. Someone forgot to implement part of a new feature, forgot that some situation could happen, forgot to check for null. Rarely interesting things show up where everything is working but not how we want it to.

### Performance: It's never what you think it is

Recently we had a bug report where a modded game would freeze for a minute for seemingly no reason and then continue like nothing went wrong. It being a heavily modded game my first reaction was to blame it on the mods doing something in a very unoptimized way. But I had to test it to figure out which mod was causing the problem. After reproducing it... I was reminded (again) that it's (almost) never what you think it is.

When a train is driven by a player the game has no idea what direction the player will drive (straight, left, or right). So, as the train is moving the game goes over the potential rails in front of the train and asks every gate it finds to open in case the train ends up driving over them. This logic was very simple: get the rail distance it would take the train to stop at its current speed and "walk" down the rails until it exceeded that distance. As it walked, tell any gates on any of those rails to open.

That logic is "correct" in that it does what it was supposed to do: open any gates that the train could drive over. What it wasn't accounting for was a rail system where everything looped back onto itself 5-10 times per junction.The time complexity for the algorithm it was using was O(N^2). That's "fine" when N is small. However, in this save file, with this rail network, and with these modded trains (with 2,500% speed bonus from modded fuel no less) it meant N ended up somewhere around 75,865. That - as it turns out - was slow.

![](https://cdn.factorio.com/assets/img/blog/fff-296-rail-junction-2.png)

Interestingly even though the algorithm was recursive it didn't stack overflow. The old algorithm was executing the "open gates on this rail" 5,755,573,057 times. Many of those requests to open gates where duplicates but the algorithm didn't care. In total it took 57 seconds for it to run all of the logic - still incredibly fast for what it was doing (1.6 million rails per game tick).

After some thinking about it; I was able to re-implement the algorithm in worse-case O(N) time which ended up executing the "open gates on this rail" logic 42,913 times and took 0.009 seconds.

### Crashing on dereferencing null? Add a null check

I love this phrase. It's both correct and incorrect at the same time. I put it right next to "Crashing from an exception? Just try-catch". It's such an easy trap to fall into: fixing a symptom instead of the cause.

Earlier this week we got a bug report about the game freezing, consuming all of the available RAM, and then crashing when it ran out of RAM. It was again a modded save file so my first instinct was to blame it on a mod. Again, I had to test it. And again... it's never what you think it is.

The crash was correct: it was running out of RAM and when that happens the game crashes and exits. But why?

The game failed to allocate memory when it tried to create a furnace smoke
Because it was trying to make 4'294'967'000~ (4 billion) smokes
Because a small negative signed integer was being converted to unsigned

Clearly the game wasn't supposed to be making 4 billion smokes. So, seeing the problem my first fix was "Casting a small negative signed integer to unsigned makes it 4 billion? If it's negative, I'll just return 0 since a negative amount of smoke makes no sense". I was about to commit this fix when the smarter part of my brain said "that makes no sense, why would this code ever say to make a negative amount of smoke?" So I kept going ... why?

 ... Because the logic to make those signed integers was "(cycle progress ...) - (last cycle progress ...)" (cycle was Because the furnace burner had made a negative amount of progress in burning the fuel (negative progress should not be possible)
Because the "remaining amount of fuel from this item to burn" was negative (negative fuel values are invalid and the game won't even reach the main menu if some mod tries to set one)
Because the mod API didn't prevent mods from doing: entity.burner.remaining_burning_fuel = -1 AND the game didn't properly clear "remaining amount of fuel from this item to burn" when the item being burnt was removed due to mod migration/removal.

So, I still repeat the phrase: "Crashing on dereferencing null? Just add a null check!" as a reminder to myself and others to always look deeper into why and never stop at the basic symptom of a problem.

As always, let us know what you think on our [forum](https://forums.factorio.com/71189).
