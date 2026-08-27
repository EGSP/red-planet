# Friday Facts #223 - Reflections on 2017

- Источник: https://factorio.com/blog/post/fff-223
- Авторы: kovarex
- Дата: 2017-12-29
- Темы: процесс
- Применимость к проекту: низкая
- Что извлечь: Итоги 2017 года в числах: строки кода, сообщения об ошибках, состав команды.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello, this is the last Friday of 2017, and as such, the last Friday facts of this year.

## 0.16.8/9 is out

It almost seams that our team never stops. To be honest, even on the Christmas eve, when everything was over and family went to sleep, I couldn't resist to stay up for little while to fix a few things :)

## The fluid balancing changing follow-up is in this release

```
- Changed fluid wagon capacity from 75k to 25k (Same as storage tank).
- Lowered fluid wagon weight from 3000 to 1000 (same as cargo wagon).
- Changed fluid wagon recipe so it requires just 1 storage tank instead of 3.
- Lowered barrel fluid capacity from 250 to 50.
  (So cargo wagon with barrels holds 20k and logistic robots are not too strong alternative to carrying fluids.)
- Decreased barrelling crafting time from 1 second to 0.2 seconds.
```

The overall feeling we had was that fluid wagon capacity is too large, fluid trains have to barely move around, and waiting for the wagon to be filled often takes too much time.

We also wanted to make sure, that wagon with barrels just can't hold more fluids compared to fluid wagon, because even when barrels are generally more work to handle, it is not what you would intuitively expect.

With the fluid wagon separation gone, the main advantage of barrels is not capacity, but versatility. They can be combined with other barrels of different kind, or items in the train, they can be limited by the inventory limit, or easily filtered by filter inserters, transported by robots, belts etc. This advantage seems to be much more intuitive and I hope it makes sense now as whole with the removal of the fluid wagon separation to you.

## The buffer chest finalization

The 0.16.8 also finishes the buffer chests. The promises from [last FFF](https://www.factorio.com/blog/post/fff-222) materialized in this update.

Requester chests always have priority over buffer chests, and by default request chests don't even request from buffers chests, only player and construction. This is what we feel is the most common use-case. But as we don't want to limit the possibilities, so the behaviour can be manually changed.

![](https://cdn.factorio.com/assets/img/blog/fff-223-requester-ui.png)

## The 2017 recapitulation

Jakub Dvorský, the head of Amanita design, said that projects tend to be more creative in the fist 3 years of the development, while the last 2 years are more about tweaking, polishing and finishing. I feel exactly the same about Factorio. Most of the creative work has been done, and we focus more and more on finishing the game.

We've said it before, that the game will be finished and released during this year and we mean it this time. There is even the joke in the office that the game is always to be released 'next summer', which never comes. But this time we *really really* mean it, we want to finish the game in 2018. Finishing doesn't necessary mean no new stuff for Factorio, it might even mean the opposite. Why? Because we will have all of our technology/graphical/design debts paid.

Debts from the past we solved this year:

- We stabilized 0.14, which meant the rewrite of the multiplayer internals based on our experience form the first version, and we now have something that seems to work quite reliably.

- We went back and created high-resolution variants of the vast majority of the graphics.

- We revisited the terrain and decoratives for the last time to make an environment we are content with

- We integrated blueprint usage into network games by allowing them to be copied between players, and allowed them to be moved between individual save games.

- We have rewritten most of the internals of how GUI works for 0.16, which should simplify additions related to GUI in the future.

- Opened the subject of interactive mini tutorials, far from finished, but we are getting the experience there as well.

- Tightened the user integration of mods, once the new mod portal site (which shouldn't be as unusably slow as the current one) is done, it will improve the usability of mods a lot.

- Improved programming productivity internally by getting rid of boost, improving compile times, extending automated/heavy/valgrind tests on the server, and starting more elaborate internal documentation. So many more problems are discovered early on, and people spend less time figuring out what is wrong.

- Improved Factorio link time in Visual studio. This was done by Rseding91, who provided the visual studio guys with Factorio sources and kept bothering them until they tested that and improved C++ link time in the 15.5 Visual studio release. The final release of Factorio with all optimisations and link time code generation took 45 minutes to compile and link, and now it takes 3.5 minutes. This sped up our release time quite a bit.

There are still some debts to be finished in 2018, mainly the GUI update, which is actually a huge project. We have very large number of different windows in the game, and if we really want to improve both the looks and the usability, we will have to put a lot of time and thought into it. Also the mini tutorials will have to be finished and few entities will probably be re-designed.

But once all these things are solved, it not only means that we could get out of early access, but also, if we ever decide to add more content to Factorio, having these things finished should pay off by letting us to focus on the new things only instead of having to fix the old ones. Another possibility, is to use the Factorio engine for completely different game or for fast prototyping of crazy ideas. Whatever we decide to do in the future, having polished base seems like an important thing to have.

## Almost 6 years of Factorio in numbers

The initial Factorio commit was done 31.3. 2012, so Factorio is in the development for 5 years 9 months.
I did this kind of comparison in [FFF-81](https://www.factorio.com/blog/post/fff-81) which was even before the steam release, I hope you don't mind if I compare it with the current state.

Our effort in numbers:

- In development for 1106 → **2099** days.

- 88 → **221** public releases.

- 14 082 → **34 686** commits in the master branch.

- 204 917 → **465 550** lines of code, 546 339 → **1 258 939** words and 7 693 483 → **17 517 675** characters, this is equivalent to 15 → **35** average books.

- 20 791 → **56 947** different sprites with 54 114 147 → **336 907 147** non empty pixels.

- 1492 → **5034** resolved bugs (only counting those reported on our forums).

- 3027 → **7670** lines in the changelog.

Results in:

- 56 500 → **341 000** Youtube videos

- 403 000 → **1 900 000** Google hits of Factorio.

- 75 146 → **303 773** forum posts

- **11 704** years of combined playtime played on steam.

- and finally 74 914 → **1 200 000+** copies sold.

This leads me to different kind of comparisons:

- 2.7 → **0.42** lines of code per one buyer

- 12.7 → **16.5** commits per day or

- 2.7 → **6** Youtube videos per one sprite

- 2.3 years of playtime per resolved bug report

- 4 months of playtime per one commit

- 9.1 days of play for each line of code

- 18.2 minutes for each pixel

I could go like this for a while :) But mainly, it makes me realize, that making changes, improving things, and fixing even small annoyances in the game is worth it.

I would also like provide the overall graph of commits frequency, some of the releases are quite visible there :)

![](https://cdn.factorio.com/assets/img/blog/fff-223-commit-graph-2.png)

As always, let us know what you think on our [forum](https://forums.factorio.com/55856).
