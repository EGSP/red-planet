# Friday Facts #356 - Blueprint library for real

- Источник: https://factorio.com/blog/post/fff-356
- Авторы: kovarex
- Дата: 2020-07-17
- Темы: личный опыт, геймдизайн, процесс
- Применимость к проекту: средняя
- Что извлечь: История утраты и восстановления мотивации одного из основателей, а также полная история развития библиотеки чертежей от первой реализации до окончательной.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
## kovarex - the story of motivation

This wall of text is about my personal struggle with Factorio and life, feel free to skip to the next subject if you wish to see the actual Factorio content.

Beginning about two years ago, I started to have these problems, it was harder and harder to force myself to work on the game and I didn't enjoy it that much. So I was looking for a way to have a break.

I know exactly when I disappeared from the Factorio development, it was August 26, 2019, the release date of World of Warcraft classic. The planned 3 weeks of playing kind of extended to be more like 3 months. One of the big reasons was, that I already had 60 level priest when I realized that tanks are so hard to come by, so I re-rolled a tank learned how to play it and levelled it to 60. It was a great fun to finish all the dungeon content and acquire the pre-bis (pre raid best in slot). This all just to find out tanks are far from a hot commodity when it comes to raiding, where you need just a few in the 40 people raid.

At this point, I thought, that I would come back to work with full power, but I just couldn't. When I tried to work, I had this strong, almost physical feeling of disgust, that was impossible to overcome. It was clear to me at this point, this is the the typical burnout situation. It is far from surprising after that many years of working that hard. The attempts to get to work were mainly motivated by guilt, and I knew well, that it is hardly a good motivation for anything. Trying to overcome it by sheer willpower would just make it worse, so I just stayed distant. The team was still working on its own and making good progress, so I was taking advantage of it and continued to have a break and spent more time with my family and on leisure activities.

As the situation was not getting that much better, there were even proposals of selling the company and getting rid of the responsibility for good. For most people, this would sound like a rational choice, but I was far from open to doing that. I generally don't like to do something just because it is the norm. The norm is to try to always keep growing exponentially, getting investors, expanding, getting more people, never stopping, never resting until you are the biggest and most horrible company, or you die trying. This approach dictates, that once you can't expand the enterprise, you need to sell it so others can grow it. And I don't like it. I didn't forget at all why we started working on this game. We wanted to make the game(s) that we couldn't find, and we wanted to have fun doing so. We wanted the game to be primarily fun for us, not for a focus group that has the most financial potential.

So, even if we faced the hypothetical decision : Either we sell it to a big publisher, or we shutdown the studio, I choose the latter, because you can't put a price tag on the fact, that we still own the game. In the latter case, we could come back to it any time when we feel like the time is right, instead of having to watch it being milked as micro transaction filled cashgrab by some company.

So, this was my lowest point personally I was generally not feeling well, and was lost in searching for purpose. One of the biggest reasons that I didn't feel well was, that I was becoming more and more lazy. When you don't have to overcome daily obstacles and annoyances, you become more and more lazy, until even the most basic things start to be huge pain in the ass, and you don't generally feel well, this is where I was.

In the meantime, I was occasionally playing some simple games with my 4.5 year old son ([Earn to die](https://armorgames.com/play/12635/earn-to-die) and [Into space 2](https://armorgames.com/play/14107/into-space-2)). I was trying to find some nice cool games that we could play together, but I didn't find anything  (feel free to give me suggestions in the comments). So I figured, that he could actually try to play Factorio.

I started a peaceful game for him, showed him how to move around, mine and craft basic stuff, and let him play. He was just running around and having a blast that he can mine trees and explore. Some other day, I joined the game, and built some small factory so basic technologies are unlocked and he could play around with that. Eventually he set himself a project to create a wall around the entire factory. He was focused and he kept at it, and 3 days later he came to me, and showed it to me, and he was so proud. Some time later, he played alone for a while, and than he showed me some very basic mining/smelting setups. It was very weird, but it worked. This is when I realised how great Factorio is for children, you can scale the skillset from very very basic up to almost infinity. He can't read, he didn't know numbers greater than 4, and yet he managed to play, and in a few days, he kind of recognized numbers up to 10 without even realizing. When I showed him how construction robots and personal requests work, he was super enthusiastic and talked about construction robots to everyone he met :).

Once he asked me "Daddy, what is this thing in the list of things I can order?" ... "This is atomic bomb" .. "Oh, I want to order it" .. "No, we don't even have it researched" ... "But, why is it in the list then, it doesn't make sense" ...  "Hmm, you are right, it doesn't, I might actually fix that". So I opened Factorio source code after a long time, and made the change, that the filter and logistic request selections didn't contain things yet to be researched (unless you force-unlock it in the settings). I made a change to Factorio, and it felt good, and I started to want more, this is how I got from the lowest point.

I wasn't yet prepared for big projects mentally, so I did few other small tweaks, and I started to visit the office occasionally, which gave me more and more energy, I was not working because of guilt, I was working because of joy again. This is when I decided to face the big elephant in the room: *The Blueprint Library*. I was scared to approach such a big project in my previous state, but now I felt brave enough. I started working on it and I was able to work in full-power mode again, the work went forward fast. I had to overcome a lot of annoying obstacles on the way, which had positive effect on my overall laziness very fast. As the new BP library started to shape up, I started to feel something almost forgotten, I was proud of what I was doing, yay :)

## The story of the Blueprint

The story of blueprint and the blueprint library development is quite long and convoluted, we mentioned it in 12 FFF already and it wasn't always quite right. But I believe, that we are getting to the final stage with the current rework. Small tweaks and improvements can be always done, but the general feeling is like "Yea, this works, finally".

### First mention

The first mention of a blueprint (apart the blueprinting mod) was in [FFF-16](https://www.factorio.com/blog/post/fff-16), 6 and half years ago!

### First blueprint implementation (0.9)

Blueprints were obviously a great upgrade when you compare it to the state of just not having them. But everything was very plain and primitive from today's perspective. You had to actually craft the blueprint (for one advanced circuit) and the setup window was not the greatest:

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-setup-first-version.png)

Note that the confirm button was the blueprint button. The exact example of us doing GUI in the logical way but not an intuitive way.
There was no way to change the blueprint once it was set up, you could only clear it (for the price of one electronic circuit).

### First improvement of the blueprint management (0.13)

Blueprints started to be important, so we added some very basic way to edit them and a way to include tiles and modules. ([FFF-131](https://www.factorio.com/blog/post/fff-131))

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-setup-version-0.13.png)

Also, the blueprint book was introduced ([FFF-108](https://www.factorio.com/blog/post/fff-108), it cost 15 advanced circuits and could hold only blueprints directly.

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-book-version-0.13.png)

### First plan of blueprint library (0.15)

The fact, that you didn't have a way to backup your blueprints and you would just plainly loose them when you died, or moved to a different game was quite harsh, so we started to work on the blueprint library. Our first mention of it was in the [FFF-156](https://www.factorio.com/blog/post/fff-156).

### First implementation (0.15)

The first implementation was pretty rough and first shown in the [FFF-161](https://www.factorio.com/blog/post/fff-161)

![](https://cdn.factorio.com/assets/img/blog/fff-161-blueprint-library-2.jpg)

Note, that the play button was the way to export the blueprint into your game as an item, so you can put it into your inventory and use it.

### First redesign

We, mainly Oxyd ([FFF-170](https://www.factorio.com/blog/post/fff-170)),  quickly realized that this needs to be more intuitive, so the way it was exported was streamlined, you just drag and drop into your inventory.

![](https://cdn.factorio.com/assets/img/blog/fff-170-drag.gif)

Another way of exporting was done secretly: When you held a blueprint record while closing the library window. It was seemingly useful as you could just grab it from the library and build, but once you were done and pressed Q to clean the cursor, the item was just spilled into your inventory. It was common at these times, that your inventory was slowly getting cluttered with random blueprints and you had to do a cleanup from time to time.

The Blueprint editing window was also improved:

![](https://cdn.factorio.com/assets/img/blog/fff-170-preview.png)

### 0.16 Blueprint preview was updated

It was "only" about connecting the belt/pipe/wall entities, but it added a lot to the understandability ([FFF-211](https://www.factorio.com/blog/post/fff-211)).

![](https://cdn.factorio.com/assets/img/blog/fff-211-0.16-blueprint.png)

### The endless discussion phase

We knew there were still a lot of problems with the blueprint library and we were desperately trying to figure out how to solve them in various different crazy ways ([FFF-249](https://www.factorio.com/blog/post/fff-249)). But a week later, we agreed on a relatively simple solution ([FFF-250](https://www.factorio.com/blog/post/fff-250) and [FFF-255](https://www.factorio.com/blog/post/fff-255)): From the player perspective, blueprints are always just items,and blueprint library is just something like a persistent chest. Quickbar, movements, stack transfers, everything works exactly like with items, and the BP library technical magic is done under the hood.

After some time (32 weeks actually), we presented a UI mockup for the planned blueprint library [FFF-282](https://www.factorio.com/blog/post/fff-282).
This was the big plan, but since 0.17 release was approaching and there were just too many other things to do, we postponed it.

### 0.17 release - More tools

In this version we added a lot of tools:

- Upgrade planner

- Copy/cut paste (with history)

- Undo

And we also extended the amount of things blueprint can handle -mainly trains ([FFF-263](https://www.factorio.com/blog/post/fff-263)):

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-setup-version-0.17.png)

We just made a few small tweaks for 0,17, to make the usage of blueprints less of a pain, mainly the ability to make a quickbar reference directly to the blueprint library. Using it created a new item that is copy of the BP library record, so you can build from it and pressing Q to clean the cursor just deleted the blueprint instead of cluttering the inventory.

But blueprint library still didn't get any real improvement.

### Current blueprint library

In 0.18 release, I improved the blueprint setup window so it matches our new GUI style:

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-setup-version-0.18.png)

But the blueprint library still didn't get any real improvement.

## New blueprint library

So, if this buildup led to nothing, it would be pretty lame so as you would expect the blueprint library is now finally getting a real improvement.

### 1) The looks

It looks nice now and mainly fits the style of the rest of the game:

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-library-new-root-view.png)

### 2) The manipulation

As it was agreed 2 years ago (fuck), the blueprints in the blueprint library are manipulated as items in every way. Twinsen forced me to agree on this way, and I wasn't that convinced at that time, but when I started to implement it, it was instantly clear, that this is way better than any other proposal. You don't have to learn anything new and just manipulate the objects exactly the same way you are used to and it just feels right.

There is quite something happening in the background when transferring Blueprints, as they are still very different types of objects in the inventory and in the blueprint library, but the user is now completely shielded from this.

### 3) The unification

All the related UI was unified to look the same, in current version for example, opening a blueprint book as an item looks very different compared to opening it in the blueprint library, and it even has different features.

### 4) The identification

Now we get to the new features, first of all, every blueprint tool has editable name, description and icons.
For Blueprint book, upgrade planner and deconstruction planner, these icons are optional, but overwrite the dynamic icons shown for them. This might be mainly useful for books that you want to just have the same preview regardless of currently selected blueprint.

When the names and descriptions become more important to the user, he can switch to the list view.

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-library-new-root-list-view.png)

Small thing that helps is that the upgrade planner now updates also related icons of the blueprints and books

[видео: fff-356-upgrade-book.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-upgrade-book.mp4)

### 5) The books in books

It is something we wanted for a long time, and it was highly requested, so now, it is possible. The maximum depth is set to 6, mainly to prevent the UI from getting out of hand.

![](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-library-new-book-view.png)

It is just logical, that iterating through the book contents works hierarchically now:

[видео: fff-356-hierarchal-scrolling.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-hierarchal-scrolling.mp4)

### 6) The tools in books and library

Since both the upgrade planner and the deconstruction planner are also kind of virtual and configurable, it just makes sense to allow them in books and the blueprint library. The preview of the book is changing when you switch between different types of objects.

[видео: fff-356-tools-in-books.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-tools-in-books.mp4)

### 7) The copy

Since blueprint manipulation is now always moving the blueprint around, never making a copy (apart the export-import workaround), we really needed this feature to make an explicit copy. The nice touch is that the copy is made based on the current unconfirmed edit of the blueprint, so you can make slightly modified versions of it quite fast.

[видео: fff-356-copy.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-copy.mp4)

### 8) The reassign

My personally most wanted feature. You can change the contents of the blueprints while the name, description, icons, quickbar links and positioning is preserved.

[видео: fff-356-reassign.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-reassign.mp4)

### Building in map

Rseding had a great timing with his feature of building blueprints directly in the game map.

[видео: fff-356-blueprint-in-map.mp4](https://cdn.factorio.com/assets/img/blog/fff-356-blueprint-in-map.mp4)

These changes are being finalised and tested, so they should be available in the upcoming weeks just in time before 1.0.

                [Discuss on our forums](https://forums.factorio.com/86936)

                [Discuss on Reddit](https://www.reddit.com/r/factorio/comments/ht4v19/friday_facts_356_blueprint_library_for_real/)
