# Friday Facts #206 - Workflow optimisation

- Источник: https://factorio.com/blog/post/fff-206
- Авторы: kovarex
- Дата: 2017-09-01
- Темы: сборка, инструменты, процесс
- Применимость к проекту: средняя
- Что извлечь: Сокращение цикла правка—сборка—проверка: замена оборудования, отказ от Boost, ускорение прогона тестов.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello Factorio players, lets start our developer to player interaction right now! :)

## Belt fast replace improvements

  Dominik is our new programmer in for a testing period. He was given the task to add support for fast-replacing splitters and belts in a reasonable way, and he ended up extending the functionality a little.

  Adding a belt junction:

![](https://cdn.factorio.com/assets/img/blog/fff-206-fast-replace-1.gif)

  Removing it:

![](https://cdn.factorio.com/assets/img/blog/fff-206-fast-replace-2.gif)

  Adding underground belt:

![](https://cdn.factorio.com/assets/img/blog/fff-206-fast-replace-3.gif)

  Removing it:

![](https://cdn.factorio.com/assets/img/blog/fff-206-fast-replace-4.gif)

  It is something I wanted in the game for a long time!

## Compile time optimisations - Technical

  Rseding explained in [FFF-201](https://www.factorio.com/blog/post/fff-201) how important it is for us to not only optimize the game for you the players, but also for us, so we can speed up our change->compile->run/test cycle.

### Step 1 - Hardware

  When I returned from my holiday this Friday, I noticed that the recompile time of Factorio on debug mode took almost 5 minutes, which is more than I was used to. We checked the CPU temperatures and memory speeds, and they are okay, but the compile time was still double of what others have on similar hardware. It turned out that it is most probably caused by slow SSD speed, which could be caused by writing a huge amount of data when working.

  From what I found on the internet, the typical SSD write capacity is something around 1000TB of data, which is not so hard to approach if one recompilation cycle of Factorio generates 5GB of data. It is quite possible, that the drive just gets slower as it approaches its limit. Actually, the lifespan my specific SSD is typically a year or less.

  Luckily, our new computers with sweet i9-7900X CPU were ready to be assembled at this time, and I'm planning to get enough memory to compile on ramdisk to prevent this problem in the future.

### Step 2 - Hardware updated

  So, after half a day of fiddling, I installed everything on a brand new computer so I could test the compilation speed. Discovering that it still took 2 minutes was not really satisfying.

### Step 3 - Getting rid of Boost

  [Boost](http://www.boost.org/) is a special kind of demon. It lures you in by giving you all these cool and simple to use features, and then it beats your soul from you by increasing compilation times absurdly.

  There are two main problems:

- They don't care much about compile times.

- They want to have everything nice and generic ad absurdum, and they even [defend it as the correct style](http://archive.is/2014.04.28-125041/http://www.boost.org/doc/libs/1_55_0/libs/geometry/doc/html/geometry/design.html).

  The result is, that changing boost::mpl::vector66 to std::variant can improve the compile time from 1:44 to 1:20 and getting rid of templates completely by using unions can decrease the compile time to 0:53. I'm talking about changing 2 headers of 2 classes in a project with 3390 files, 410k lines of code and 15Mb of source code.

  Everything that was compiled to Factorio, GUI, graphics library, networking, entity logic, scripting, modding, logistic system... all these things together took the same time to compile as two instances of boost::mpl::vector. Our current goal is to get rid of the Boost library completely.

  The conclusion is that moving from 5 minutes compilation times to sub 1 minute in one week feels good, and it is worth the trouble to improve it from time to time (until a better language for our purpose is invented, which Jai could be someday).

## Test runtime optimisations

  Another thing that was done to improve the speed of our change->compile->run/test cycle was the improvement of the automated test run speed. Rseding made a great feature that runs the tests in parallel, which is especially useful with our new 20 thread systems.

### Debug mode:

| Standard: | 270.23 seconds. |
|---|---|
| Fork: | 45.64 seconds. |
| Difference: | **5.92** times faster |

### Release mode:

| Standard: | 58.94 seconds. |
|---|---|
| Fork: | 17.56 seconds. |
| Difference: | **3.35** times faster (limited by slowest suite) |

### Heavy mode:

| Standard: | 7456.27 seconds |
|---|---|
| Fork: | 877.82 seconds |
| Difference: | **8.49** times faster (limited by slowest suite) |

## High res pipe entities

  As pipes and pumps are high resolution already, it only makes sense to upgrade the entities that tile with that, such as storage tanks and pumpjacks:

![](https://cdn.factorio.com/assets/img/blog/fff-206-pipe-entities.gif)

As always, let us know your thoughts, ideas and feedback on our [forum](https://forums.factorio.com/52353).
