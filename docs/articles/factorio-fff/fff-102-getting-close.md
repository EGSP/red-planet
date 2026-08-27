# Friday Facts #102 - Getting close

- Источник: https://factorio.com/blog/post/fff-102
- Авторы: Tomas
- Дата: 2015-09-04
- Темы: инфраструктура, процесс
- Применимость к проекту: низкая
- Что извлечь: Отказ службы учётных записей под нагрузкой и разбор причин; портал модов; замечания об истощении сил после успеха.

Текст статьи принадлежит Wube Software; копия сохранена для чтения без сети.

---
Hi everyone,

the summer is slowly preparing to pack its stuff. Overall weather is still very pleasant however mornings and evenings are getting colder and days are getting shorter. So is our list of bugs on the forum actually! With current 0.12.6 there are about 30 open bug reports in total. The pendulum is swinging back and forth all the time but it seems like a good start for having stable release within few iterations. At the moment it looks like that development work for the release will be done before the gfx work - we are commited to ship a full set of new technology and equipment sprites for the stable 0.12.

### Account crisis

So I have had the whole blog post written down but I had to add this paragraph afterwards. Today I was testing our new mini web application that will handle logins from all our services (website, updater, game itself, forum, mod database). Due to my ignorance I have ran some debugging code on the production database. That is generally a VERY silly thing to do. To make things more interesting the debugging code did (among other things) clean the user data collection - basically removing all the records about users in the database. While this is standard on the development database it is a kind of a disaster on the production database.

Soon after this, we started receiving emails from people who couldn't log in, because their accounts are just not there. Our database provider claims to make daily backups. I have contacted them immediately and asked for the backup of the database to restore the collection. At the moment we are waiting for the answer. I am very sorry for the inconvenience my carelessness might have caused you. In case the backup arrives, the issue should be solved within relatively short time. If it doesn't we will have to improvise. Let's hope it does arrive=)). Lesson learned the hard way: typing "user.remove()" should trigger VERY careful behavior.

EDIT: The users collection has been restored from the backup from yesterday morning. YAY! In case you are one of the few users whose account has been created in the meantime please contact us to resolve this. Big thanks goes to awesome guys at [mongolab](https://www.mongolab.com) where we host our data and who have been very fast and helpful in the backup restoration process.

### Modding portal

For a while now there has been a community modding database called [FactorioMods](http://www.factoriomods.com) in development. Actually we have had plans to do our own database for quite some time. Hence this was almost like a sign to us=)) We got in touch with the project owner and started talking. The result is that we will work together on the official Factorio modding portal. The basic functionality will be very similar to what is there already at Factoriomods. However the whole website will have tighter integration with the Factorio infrastructure - namely using Factorio authentication and user management as well as our CDN for mod delivery. There are plenty of other small features that are planned. We will bring you the details once the progress is more apparent. The current plan (plan is a plan and hence it might change =)) is to bring basic modding portal and its game integration (listing, viewing and downloading mods) in the 0.13.

### Hard time for Notch

Since we are in the silly season (or a "cucumber season" as it is called here) let's have a look at a topic which only remotely touches the current state of Factorio affairs. It is about "dealing with success".

Some of you might have caught an article about, what the original Minecraft creator Notch, is [currently going through](http://www.cnet.com/news/billionaire-who-sold-minecraft-to-microsoft-is-sad-and-lonely/). In short, after selling Mojang for more money than one can spend in a lifetime (though anything is possible) he is in his own words "isolated" and not giving a happy impression at all. It all seems to come down to what many have already indicated. Material success (i.e. making lots of money) on itself is by no means an assurance of happy and fulfilling life. What does this have to do with Factorio?

### Focusing on the process

Well, we are not anywhere even close to a fraction of Mojang's and Notch's "success". However it is still a relevant lesson. Things are going pretty well for us. The studio is full of people who enjoy what they do, the game is selling alright, there is rooms for solid old-school development as well as experimentation, days full of stress and uncertainty are long gone ... But Notch's case is here to remind us to keep things easy and focus more on enjoying the process of creating the game than trying to get to the "finishing line" as soon as possible.

Now don't get me wrong, I am not saying that Notch / Mojang sacrified the process for the result or anything along those lines. Also I am not holding out Notch as an example of a failure or something. I am just trying to look calmly at the situation and learn from it. The message for me is simple: If we metaphorically compare creation of the game (or living a life?) to a run then it is not about getting to the finishing line as fast as possible but about enjoying the steps along the way. Because if we don't we might suddenly end up in the finish not knowing what to do. And maybe by focusing on the steps rather than the finish line the run will be worth it and we might even come out of it with a good time=) Again this is not anything new or revolutionary, it is kind of a common knowledge but it is interesting to see it bubble up here and there occasionally.

Also, just to be quite honest, things are not always happy and blue. We all have our ups and downs. We have talked about this with kovarex and Albert quite a bit. "Pulling the Factorio cart" for this long has been tiring and we might take some break after the project is finished to see what we want to do next. There are quite come crazy / ambitious ideas involved so it sounds like a good topic for future FFF=)

### GFX reinforcement

Our GFX team has been a single man team (Albert :)) again for a few weeks since we let go of our second artist some time ago. However for the project of Factorio size, one man (even though Albert is very capable) is just not enough. So we have been actively looking for a reinforcement. Some time ago we got contacted by a Factorio player and an avid Open TTD modder (see an example [project](http://dev.openttdcoop.org/projects/nuts) of his) from Czech republic who was very keen to come and work with us. Last week he was here with us in the office for kind of a testing period to see how well they will understand each other with Albert. That went just fine so we agreed on further full time cooperation. His name is Vaclav and he will be starting in the beginning of October. His testing task was the power switch entity that will come in the 0.13. You can judge his first contribution to the game below.

In the meantime Albert has been continuing his work on the Technology and Equipment images. It is a LOT of work because we have been accumulating graphical debt for quite some time in this area. However now is the time to erase this debt and bring up the quality bar in this part of the game to the par with the rest.

![](https://cdn.factorio.com/assets/img/blog/fff-102-power-switch-1.gif)

![](https://cdn.factorio.com/assets/img/blog/fff-102-power-switch-2.gif)

That being said, let us know if what needs to be known at [our forums](https://forums.factorio.com/forum/viewtopic.php?t=15520).
