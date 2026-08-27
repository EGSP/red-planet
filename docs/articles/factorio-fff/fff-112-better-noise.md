# Friday Facts #112 - Better noise

- Источник: https://factorio.com/blog/post/fff-112
- Авторы: Tomas
- Дата: 2015-11-13
- Темы: генерация мира, производительность, шум
- Применимость к проекту: высокая
- Что извлечь: Оптимизация вычисления шума при генерации карты: значения считаются блоками, а не по отдельным точкам, что меняет стоимость на порядок.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello hello,

I hope that the powerful constellation of Friday the 13th has brought you good luck or at least good humour. We have seen quite sharp decline in some Factorio related statistics in the past days so it seems that the AAA titles invasion starts to be felt ... many of you playing Fallout 4?

## 0.13 in focusTomas

The 0.12.17 candidate is out and it is looking "stablish" so far. Hence the 0.13 development is slowly taking up speed with multiple people starting on their tasks picked from the list shared in the [previous FFF](https://www.factorio.com//blog/post/fff-111).

Kovarex is working on the better rail building which is looking really cool, and some of the screenshots he made feel like [mathematical art](https://www.google.cz/search?q=mathematical+art&source=lnms&tbm=isch). Don't miss the next FFF for more details! Twinsen is working on the long wanted power switch and Oxyd is going to start the optimizations in the AI and path finding.

I have been working on the Multiplayer Matching server and its integration in the game for some time. Originally this was supposed to be the main topic of the blog post with a paragraph in the end about the Map generation optimization, which is Cube's stuff. However his contribution to the Friday Facts ended up being much longer than one paragraph (and interesting though very technical), so it has became the main theme=)) The details on the Matching Server will follow later when it is more developed anyway=)

## Optimizing the noiseCube

Warning: technical stuff ahead :) .

Until now, we have used a slightly modified version of Ken Perlin's
[Improved Noise](http://mrl.nyu.edu/~perlin/noise/) as a base smooth noise function for generating terrain.
While this algorithm worked well for us, it is originally intended to provide a
procedural texture for 3D graphics, not for generating large 2D surfaces, so for 0.13.0
we will replace it by a custom noise algorithm.

The main idea behind optimizing the generator is to not generate values for every
tile separately, but to group all calculations required for the whole chunk and reuse
the intermediate steps between individual tiles.

But since we'll be already rewriting the whole code anyway (and since Cube never
misses an opportunity to geek out over coherent noise functions), we'll also be
replacing Perlin noise by something else -- a mix of
[Perlin noise](https://flafla2.github.io/2014/08/09/perlinnoise.html)
 and the newer
[simplex noise](http://webstaff.itn.liu.se/~stegu/simplexnoise/simplexnoise.pdf).
Our noise will have the simple square grid from Perlin noise,
the gradient summation idea from simplex noise
and a very cool hash function from '[](https://sci.utah.edu/publications/SCITechReports/UUSCI-2008-001.pdf)Better Gradient
Noise'.

![](https://cdn.factorio.com/assets/img/blog/fff-112-noise-1.png)

The picture in todays FFF (split in two because it was too wide) shows some data from testing of the various hash functions
using a [python prototype](https://gist.github.com/bluecube/1d31dfe23d8fb590f7bc).
These are used in the noise generator to combine the coordinates of each point
and select a pseudo random gradient.

First row shows the raw hash, second row a single octave of the noise generated from
this hash and in the third row we have the Fourier transform of the second row. So what is used in the game itself?
The second row represents a single octave or a layer. In the game we use multiple layers that are combined to produce final noise.
From this perspective the better noise looks as a good candidate to use (good looking results in little time).

Some more explanations on functions used:

- **md5** is the well known cryptographic hash function. As good result as it gets (for our purposes), but **very** slow. It's not a real candidate for terrain generation, but included for comparison.

- **perlin**: the original hash function for Perlin noise.

- **better**: hash function from [](https://sci.utah.edu/publications/SCITechReports/UUSCI-2008-001.pdf)Better Gradient Noise.

- **[fnv1a](http://www.isthe.com/chongo/tech/comp/fnv/#FNV-1a)**

- **[djb2](http://www.cse.yorku.ca/~oz/hash.html#djb2)**

- **blackmoons**: A hash function from a [value noise](https://en.wikipedia.org/wiki/Value_noise) by BlackMoons

- **murmur**: based on [MurmurHash](https://en.wikipedia.org/wiki/MurmurHash) with some modifications because of our fixed input size.

- **cube**: Cube's "I don't know what I'm doing exactly, but it seems to work" hash.

The times with each of the hash functions show how long it took to generate 8k x 8x large
matrix of the raw noise, but because the implementation is in python and heavily depends
on numpy, these don't say much about the final runtimes of C++ implementation.

![](https://cdn.factorio.com/assets/img/blog/fff-112-noise-2.png)

## The visual effectsTomas

Our GFX department (namely Vaclav) has been working on some visual effects recently. These would be fire and electricity for now (and some more beams / particles / explosions coming later). Below is a preview of the power switch with electricity running through it. It will look a bit better in the game due to a different blending mode used.

![](https://cdn.factorio.com/assets/img/blog/fff-112-power-switch.gif)

Not completely lost in the Noise and map generation stuff? Share your thoughts [on the forums](https://forums.factorio.com/forum/viewtopic.php?t=17749).
