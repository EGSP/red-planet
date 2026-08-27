# Friday Facts #264 - Texture streaming

- Источник: https://factorio.com/blog/post/fff-264
- Авторы: posila
- Дата: 2018-10-12
- Темы: графика, производительность, память
- Применимость к проекту: высокая
- Что извлечь: Потоковая подгрузка текстур и виртуальная текстура: работа при нехватке видеопамяти без падения частоты кадров.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hello, it is me, posila, with another technical article. Sorry.

## Bitmap Cache

In 0.16, we added graphics option mysteriously called "Low VRAM mode", it enables a basic implementation of texture streaming, but I didn't want to call it that way, because I feared its poor performance would give texture streaming a bad name. How it worked? Every sprite has specified some priority, and the "Video memory usage" option - despite its name - controls which priorities of sprites are included in the sprite atlases. What happens to sprites that don't go to atlas? They are loaded as individual sprites. Normally, these sprites are allocated into texture objects. The reasoning behind this is that graphics driver has chance to decide how it wants to layout these small textures in memory, instead of it being forced to work with huge atlases.

![](https://cdn.factorio.com/assets/img/blog/fff-264-atlas.png)

*What part of a sprite atlas looks like.*

When you enable "Low VRAM mode", non-atlas sprites are loaded only to RAM, and texture is allocated for them only when used during rendering. We called class that handled this BitmapCache and there was maximum limit of how many mega-bytes of texture data the bitmap cache could use at once. When this limit was reached, it failed to convert memory bitmap to video texture, and Allegro's drawing system would fallback to software rendering, which absolutely tanked FPS, but this didn't happen... most of the time.

So apart from the obvious problem of falling back to software renderer (which we don't have any more after the graphics rewrite, so the game would crash or skip a sprite if this happened), there are other performance issues. Most sprites have a unique size, so we can't reuse textures for different sprites. Instead, when a sprite needs to be converted to a texture, a new texture is allocated, and when the sprite is evicted from the cache, its texture is destroyed. Creating and destroying textures considerably slows down rendering. The way we do it also fragments memory, so all of the sudden it may fail allocate new texture because there is no large enough consecutive block of memory left. Also, since our sprites are not in an atlas, sprite batching doesn't work and we get another performance hit from issuing thousands of draw calls instead of just hundreds.

I considered it to be an experiment, and actually was kind of surprised that its performance was not as bad as I expected. Sure, it might cause FPS drop to single digits for a moment from time to time, but overall the game was playable (I have a long history of console gaming, so my standards as a player might not be very high :)).

Can we make it good enough, so it wouldn't be an experimental option any more, but something that could be enabled by default? Let's see. The problem is texture allocations, so let's allocate one texture for the entire bitmap cache - it would be a sprite atlas that we would dynamically update. That would also improve sprite batching, but when I started to think how to implement it, I quickly ran into a problem dealing with the fragmentation of space. I considered doing "defragmentation" from time to time, but it started to seem like an overwhelming problem, with a very uncertain result.

## Virtual texture mapping

As I mentioned in [FFF-251](https://www.factorio.com/blog/post/fff-251), it is very important for our rendering performance to batch sprite draw commands. If multiple consecutive draw commands use the same texture, we can batch them into a single draw call. That's why we build large sprite atlases. Virtual texture mapping - a texture streaming technique popularized by id Software as Mega Textures, seems like a perfect fit for us. All sprites are put into a single virtual atlas, the size of which is not restricted by hardware limits. You still have to be able to store the atlas somewhere, but it doesn't have to be a consecutive chunk of memory. The idea behind it is the same as in virtual memory - memory allocations assign a virtual address that maps to some physical location that can change under the hood (RAM, page file, etc.), sprites are assigned virtual texture coordinates that are mapped to some physical location.

The virtual atlas is divided into tiles or pages (in our case 128x128 pixels), and when rendering we will figure out which tiles are needed, and upload them to a physical texture of much smaller dimensions than the virtual one. In the pixel shader, we then transform virtual texture coordinates to physical ones. To do that, we need an indirection table that says where to find the tiles from the virtual texture in the physical one. It is quite a challenge for 3D engines to figure out which virtual texture pages are needed, but since we go through the game state to determine which sprites should be rendered, we already have this information readily available.

That solves the problem of frequent allocations - we have one texture and just update it. Also, since all the sprites share the same texture coordinate space, we can batch draw calls that use them. Great!

However we could still run out of space in the physical texture. This is more likely if player zooms out a lot, as lot more different sprites can be visible at once. Well, if you zoom out, sprites are scaled down, and we don't need to render sprites in their full resolution. To exploit this, the virtual atlas has a couple levels of details ([mipmaps](https://en.wikipedia.org/wiki/Mipmap)), which are the same texture scaled down to 0.5 size, 0.25 size, etc. and we can stream-in only the mipmap levels that are needed for the current zoom level. We can use lower mipmap levels also if you are zoomed in and there are just too many sprites on the screen. We can also utilize the lower details to limit how much time is spent for streaming per frame to prevent stalls in rendering when a big update is required.

The Virtual atlas technique is big improvement over the old "Low VRAM mode" option, but it is still not good enough. In the ideal case, I would like it to work so well, we could remove low and very-low sprite quality options, and everyone would be able to play the game on normal. What prevents that from happening is that the entire virtual atlas needs to be in RAM. Streaming from HDD has very high latency, and we are not sure yet if it will be feasible for us to do without introducing bad sprite pop-ins, etc.

If you'd like to learn how virtual texture mapping works in more detail, you can read the paper [Advanced Virtual Texture Topics](http://developer.amd.com/wordpress/media/2013/01/Chapter02-Mittring-Advanced_Virtual_Texture_Topics.pdf), or possibly even more in-depth [Software Virtual Textures](http://www.mrelusive.com/publications/papers/Software-Virtual-Textures.pdf).

## GPU rendering performance

The main motivation behind texture streaming, is to make sure the game is able to run with limited resources, without having to reduce visual quality too much. According to the Steam hardware survey, almost 60% of our players (who have dedicated GPU), have at least 4GB of VRAM and this number grows as people upgrade their computers:

![](https://cdn.factorio.com/assets/img/blog/fff-264-vram-graph.png)

We have received quite a lot of bug reports about rendering performance issues from people with decent GPUs, especially since we started adding high-resolution sprites. Our assumption was that the problems were caused by the game wanting to use more video memory than available (the game is not the only application that wants to use video memory) and the graphics driver has to spend a lot of time to optimize accesses to the textures.

During the graphics rewrite, we learned a lot about how contemporary GPUs work (and are still learning), and we were able to utilize the new APIs to measure how much time rendering takes on a GPU.

To simply draw a 1920x1080 image to a render target of the same size, it takes:

- ~0.1ms ~0.07ms on GeForce GTX 1060.

- ~0.15 ms ~0.04ms on Radeon Vega 64.

- ~0.2ms on GeForce GTX 750Ti or Radeon R7 360.

- ~0.75ms on GeForce GT 330M.

- ~1ms on Intel HD Graphics 5500.

- ~2ms on Radeon HD 6450.

This seems to scale linearly with the number of pixels written, so it would take ~0.4ms for the GTX 1060 to render the same thing in 4K.

*Update: Several people wondered how come Vega 64 ended up slower than GTX 1060. I originally ran the tests with 60 FPS cap, so I re-ran the tests without the cap and got ~0.04ms on Vega, and ~0.07ms on GTX 1060. So the cards were probably operating in some kind of low-power mode, since they were idle for huge part of the frame. You should still take my measurements with big grain of salt, I didn't attempt to be scientific about it, I just wanted to illustrate huge performance difference between different GPUs people might want to use to play the game.*

That's pretty fast, but our sprites have a lot of semi-transparent pixels. We also utilize transparency in other ways - from drawing ghosts and applying color masks, to drawing visualizations like logistic area or turret ranges. This results in large amount of overdraw - pixels being written to multiple times. We knew overdraw was something to avoid, but we didn't have any good data on how much it happens in Factorio, until we added the Overdraw visualisation:

![](https://cdn.factorio.com/assets/img/blog/fff-264-scene.png)

The game scene being rendered.

![](https://cdn.factorio.com/assets/img/blog/fff-264-overdraw.png)

Overdraw visualisation (cyan = 1 draw, green = 2, red >= 10).

![](https://cdn.factorio.com/assets/img/blog/fff-264-overdraw-discard.png)

Overdraw visualisation when we discard transparent pixels.

Interestingly, discarding completely transparent pixels didn't seem to make any performance difference on modern GPUs, including the Intel HD. Drawing sprites with a lot of completely transparent pixels is faster than an opaque sprite without having to explicitly discard transparent pixels with shaders. However, it did make difference on Radeon HD 6450 and GeForce GT 330M, so perhaps modern GPUs throw away pixels that wouldn't have any effect on the result automatically?

Anyway, a GTX 1060 renders a game scene like this in 1080p in 1ms. That's fast, but it means in 4K it would take 4ms, 10ms on integrated GPUs, and more that a single frame worth of time (16.66ms) on old, non-gaming GPUs. No wonder, scenes heavy on smoke or trees can tank FPS, especially in 4K. Maybe we should do something about that...

As always, let us know what you think on our [forum](https://forums.factorio.com/62921).
