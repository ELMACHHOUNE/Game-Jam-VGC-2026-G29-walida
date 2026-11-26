-> ch2_ouadya_neighborhood

=== ch2_ouadya_neighborhood ===
#bg:neighborhood1
#bgm:event:/BGM/CalmMusic

<i>Footsteps and talking</i>

I look around the neighborhood. I notice Lalla Khadija standing outside her shop.
We make eye contact.

#char:khadija:neutral
#speaker:khadija
Oh! Um Salam! Can I help you with something?

#speaker:narrator
Salam Lalla Khadija… it's Abbass, Zohra's son.

#char:khadija:sad
#speaker:khadija
Z... Zohra's son?
Oh… it's been a while since I heard that name…
Oh, um... I am sorry about the news…

#sfx:event:/SFX/Distortion

+ [It's okay…]
    -> khadija_understanding

+ [Please don't remind me.] #denial: 1
#glitch 0.5
    -> khadija_understanding

=== khadija_understanding ===
#char:khadija:neutral
#speaker:khadija
I understand how you feel, a wildi...
I grew up with Zohra in this neighborhood. We both played hide and seek, went to 7anot to buy Bimo, chased down cats and dogs…
She was such a great woman…

#sfx:event:/SFX/Distortion

+ [She was a great woman!]
    -> khadija_great_woman

+ [Don't say that! She's still a great woman!] #denial: 1
#glitch:0.5
    -> khadija_great_woman

=== khadija_great_woman ===
#char:khadija:neutral
#speaker:khadija
Lalla Khadija: Oh, Abbass, I… didn't mean it like that… I know it's hard for you to accept that—

+ [I am trying…]
    -> END

+ [She will be okay.] #denial: 1
#glitch:0.5
    -> END