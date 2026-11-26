-> ch2_ouadya_neighborhood_slide2

=== ch2_ouadya_neighborhood_slide2 ===
[After my conversation with Lalla Khadija, I hear someone calling out to me, stopping me in my tracks.]

#char:hicham:happy
#speaker:hicham
Waaaaaaaa Abbass! Is this really you?

#speaker:narrator
Oh, umm Salam… do I know you?

#char:hicham:happy
#speaker:hicham
That's it, lghorba made you lose your memories. It's me, Hicham. We took the concours together, for the scholarship abroad.

#speaker:narrator
Ahh, okay.

#speaker:hicham
I didn't pass, but it's okay. I made a life here.

#char:hicham:sad
#speaker:hicham
I heard the news about your mother from Lalla Khadija…

#sfx:event:/SFX/Distortion

+ [Everyone seems to know. I guess I'm the last one, huh…]
    -> hicham_understanding

+ [Don't worry, she will be okay.] #denial: 1
#glitch:0.5
    -> hicham_understanding

=== hicham_understanding ===
#char:hicham:sad
#speaker:hicham
Zohra wanted us to study together and become roommates… She treated me like her own son…

You should go visit Butcher Allal. He still has his shop by the souk. He was devastated when he heard the news.

#sfx:event:/SFX/Distortion

+ [Thanks, Hicham. She always wanted me to see him.]
    -> hicham_goodbye

+ [She did mention him to me this morning on the phone… I'll go see him.] #denial: 1
#glitch: 0.5
    -> hicham_goodbye

=== hicham_goodbye ===
-> END