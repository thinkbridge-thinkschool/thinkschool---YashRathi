using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
using QuotesApi.Models;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Pull secrets from Azure Key Vault before any other configuration is read.
// In production the managed identity (or workload identity) authenticates
// automatically via DefaultAzureCredential — no credential in code or config.
// Locally, set AZURE_KEYVAULT_URI in user-secrets or launchSettings and log in
// with 'az login'. Key Vault secret names use '--' as a hierarchy separator
// (e.g. "ApplicationInsights--ConnectionString" → "ApplicationInsights:ConnectionString").
var keyVaultUri = builder.Configuration["AzureKeyVault:Uri"];

if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(
        new Uri(keyVaultUri),
        new DefaultAzureCredential());
}

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddPolicy("SwaOrigins", policy =>
    {
        policy
            .WithOrigins(
                "https://yellow-meadow-0bd239f00.7.azurestaticapps.net",
                "http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Push ASP.NET Core's TraceIdentifier into Serilog's log context
// so every log line in a request carries the same TraceId property.
app.Use(async (ctx, next) =>
{
    using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        await next();
});

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                    // /openapi/v1.json
    app.MapScalarApiReference();         // /scalar/v1
}

app.UseCors("SwaOrigins");

app.UseMiddleware<ExceptionMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Email = "test@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password123")
        });

        db.SaveChanges();
    }

    // Seed 20 authors × 10 quotes = 200 rows. Shuffled so every page shows a mix of authors.
    // Skip in the Testing environment so integration tests start with an empty Quotes table.
    if (!app.Environment.IsEnvironment("Testing") && !db.Quotes.Any())
    {
        var now = DateTimeOffset.UtcNow;
        var authorQuotes = new Dictionary<string, string[]>
        {
            ["Marcus Aurelius"] = [
                "You have power over your mind, not outside events. Realize this, and you will find strength.",
                "The impediment to action advances action. What stands in the way becomes the way.",
                "Waste no more time arguing about what a good man should be. Be one.",
                "The soul becomes dyed with the colour of its thoughts.",
                "Our life is what our thoughts make it.",
                "Nowhere can man find a quieter or more untroubled retreat than in his own soul.",
                "Do every act of your life as though it were the very last act of your life.",
                "If it is not right, do not do it; if it is not true, do not say it.",
                "Begin at once to live, and count each separate day as a separate life.",
                "Accept the things to which fate binds you, and love the people with whom fate brings you together."
            ],
            ["Albert Einstein"] = [
                "Imagination is more important than knowledge.",
                "Life is like riding a bicycle. To keep your balance, you must keep moving.",
                "In the middle of difficulty lies opportunity.",
                "The true sign of intelligence is not knowledge but imagination.",
                "Try not to become a man of success. Rather become a man of value.",
                "I have no special talent. I am only passionately curious.",
                "Logic will get you from A to B. Imagination will take you everywhere.",
                "A person who never made a mistake never tried anything new.",
                "Only a life lived for others is a life worthwhile.",
                "The measure of intelligence is the ability to change."
            ],
            ["Maya Angelou"] = [
                "People will forget what you said, people will forget what you did, but people will never forget how you made them feel.",
                "Nothing will work unless you do.",
                "My mission in life is not merely to survive, but to thrive.",
                "You can't use up creativity. The more you use, the more you have.",
                "Nothing can dim the light which shines from within.",
                "There is no greater agony than bearing an untold story inside you.",
                "Try to be a rainbow in someone's cloud.",
                "We may encounter many defeats but we must not be defeated.",
                "You alone are enough. You have nothing to prove to anybody.",
                "I've learned that people will forget what you said, but never how you made them feel."
            ],
            ["Steve Jobs"] = [
                "Your time is limited, so don't waste it living someone else's life.",
                "Stay hungry, stay foolish.",
                "Innovation distinguishes between a leader and a follower.",
                "The only way to do great work is to love what you do.",
                "Creativity is just connecting things.",
                "Design is not just what it looks like. Design is how it works.",
                "Quality is more important than quantity. One home run is much better than two doubles.",
                "Simple can be harder than complex. You have to work hard to get your thinking clean.",
                "We're here to put a dent in the universe. Otherwise why else even be here?",
                "The people who are crazy enough to think they can change the world are the ones who do."
            ],
            ["Oprah Winfrey"] = [
                "The biggest adventure you can take is to live the life of your dreams.",
                "Turn your wounds into wisdom.",
                "You become what you believe.",
                "The more you praise and celebrate your life, the more there is in life to celebrate.",
                "Create the highest, grandest vision possible for your life.",
                "Be thankful for what you have; you'll end up having more.",
                "Real integrity is doing the right thing, knowing that nobody's going to know.",
                "The key to realizing a dream is to focus not on success but significance.",
                "Every time you suppress some part of yourself, you are in essence altering the soul.",
                "Surround yourself only with people who are going to lift you higher."
            ],
            ["Elon Musk"] = [
                "When something is important enough, you do it even if the odds are not in your favor.",
                "Failure is an option here. If things are not failing, you are not innovating enough.",
                "The first step is to establish that something is possible; then probability will occur.",
                "I think it's very important to have a feedback loop.",
                "If you get up in the morning and think the future is going to be better, it is a bright day.",
                "Persistence is very important. You should not give up unless you are forced to give up.",
                "Work like hell. Put in 80 to 100 hour weeks every week.",
                "Some people don't like change, but you need to embrace change if the alternative is disaster.",
                "I could either watch it happen or be a part of it.",
                "The path to the CEO's office should not be through the CFO's office."
            ],
            ["J.K. Rowling"] = [
                "It is our choices that show what we truly are, far more than our abilities.",
                "Happiness can be found even in the darkest of times, if one only remembers to turn on the light.",
                "It matters not what someone is born, but what they grow to be.",
                "We're all human, aren't we? Every human life is worth the same, and worth saving.",
                "Dumbledore says people find it far easier to forgive others for being wrong than being right.",
                "The truth is a beautiful and terrible thing, and should therefore be treated with caution.",
                "Fear of a name increases fear of the thing itself.",
                "You sort of start thinking anything's possible if you've got enough nerve.",
                "It is impossible to live without failing at something, unless you live so cautiously that you might as well not have lived at all.",
                "Rock bottom became the solid foundation on which I rebuilt my life."
            ],
            ["Mother Teresa"] = [
                "If you judge people, you have no time to love them.",
                "Not all of us can do great things. But we can do small things with great love.",
                "Spread love everywhere you go. Let no one ever come to you without leaving happier.",
                "The most terrible poverty is loneliness, and the feeling of being unloved.",
                "If you are humble nothing will touch you, neither praise nor disgrace.",
                "We shall never know all the good that a simple smile can do.",
                "Peace begins with a smile.",
                "Do not wait for leaders; do it alone, person to person.",
                "I alone cannot change the world, but I can cast a stone across the waters to create many ripples.",
                "Kind words can be short and easy to speak, but their echoes are truly endless."
            ],
            ["Rumi"] = [
                "What you seek is seeking you.",
                "Yesterday I was clever, so I wanted to change the world. Today I am wise, so I am changing myself.",
                "The wound is the place where the light enters you.",
                "Out beyond ideas of wrongdoing and rightdoing, there is a field. I'll meet you there.",
                "Don't grieve. Anything you lose comes round in another form.",
                "Sell your cleverness and buy bewilderment.",
                "Let yourself be silently drawn by the strange pull of what you really love.",
                "Your task is not to seek for love, but merely to seek and find all the barriers within yourself.",
                "Do not be satisfied with the stories that come before you. Unfold your own myth.",
                "Live where you fear to live. Destroy your reputation. Be notorious."
            ],
            ["Seneca"] = [
                "It is not that I'm so smart, it's just that I stay with problems longer.",
                "Luck is what happens when preparation meets opportunity.",
                "We suffer more in imagination than in reality.",
                "Waste no more time arguing about what a good man should be. Be one.",
                "The whole future lies in uncertainty: live immediately.",
                "Begin at once to live, and count each day as a separate life.",
                "It is not the man who has too little, but the man who craves more, that is poor.",
                "Life is long if you know how to use it.",
                "No person has the power to have everything they want, but it is in their power not to want what they don't have.",
                "If you really want to escape the things that harass you, what you're needing is not to be in a different place but to be a different person."
            ],
            ["Frida Kahlo"] = [
                "At the end of the day, we can endure much more than we think we can.",
                "I tried to drown my sorrows, but the bastards learned how to swim.",
                "I paint flowers so they will not die.",
                "Feet, what do I need you for when I have wings to fly?",
                "Take a lover who looks at you like maybe you are magic.",
                "Nothing is absolute. Everything changes, everything moves, everything revolves, everything flies and goes away.",
                "I am my own muse. I am the subject I know best.",
                "I don't paint dreams or nightmares, I paint my own reality.",
                "The most important part of the body is the brain. Of my face, I like the eyebrows and eyes.",
                "I never paint dreams or nightmares. I paint my own reality."
            ],
            ["Ernest Hemingway"] = [
                "There is no friend as loyal as a book.",
                "The best way to find out if you can trust somebody is to trust them.",
                "The world breaks everyone, and afterward, many are strong at the broken places.",
                "Courage is grace under pressure.",
                "The most painful thing is losing yourself in the process of loving someone too much.",
                "I love sleep. My life has the tendency to fall apart when I'm awake, you know?",
                "We are all broken, that's how the light gets in.",
                "Never go on trips with anyone you do not love.",
                "The first draft of anything is garbage.",
                "Write hard and clear about what hurts."
            ],
            ["Walt Disney"] = [
                "All our dreams can come true, if we have the courage to pursue them.",
                "The way to get started is to quit talking and begin doing.",
                "It's kind of fun to do the impossible.",
                "Why worry? If you've done the very best you can, worrying won't make it any better.",
                "All the adversity I've had in my life has strengthened me.",
                "The more you like yourself, the less you are like anyone else, which makes you unique.",
                "Laughter is timeless, imagination has no age, dreams are forever.",
                "When you believe in a thing, believe in it all the way, implicitly and unquestionably.",
                "First, think. Second, believe. Third, dream. And finally, dare.",
                "Around here we don't look backwards for very long. We keep moving forward."
            ],
            ["Oscar Wilde"] = [
                "Be yourself; everyone else is already taken.",
                "To live is the rarest thing in the world. Most people just exist.",
                "Always forgive your enemies; nothing annoys them so much.",
                "We are all in the gutter, but some of us are looking at the stars.",
                "Every saint has a past, and every sinner has a future.",
                "I can resist everything except temptation.",
                "The truth is rarely pure and never simple.",
                "A man who does not think for himself does not think at all.",
                "Man is least himself when he talks in his own person. Give him a mask, and he will tell you the truth.",
                "Experience is one thing you can't get for nothing."
            ],
            ["Mark Twain"] = [
                "The secret of getting ahead is getting started.",
                "If you tell the truth, you don't have to remember anything.",
                "Whenever you find yourself on the side of the majority, it is time to reform.",
                "A lie can travel halfway around the world while the truth is putting on its shoes.",
                "The human race has one really effective weapon, and that is laughter.",
                "Twenty years from now you will be more disappointed by the things you didn't do.",
                "Keep away from people who try to belittle your ambitions.",
                "The more I learn about people, the more I like my dog.",
                "Never argue with stupid people, they will drag you down to their level.",
                "It's not the size of the dog in the fight, it's the size of the fight in the dog."
            ],
            ["Mahatma Gandhi"] = [
                "Be the change you wish to see in the world.",
                "An eye for an eye will only make the whole world blind.",
                "The weak can never forgive. Forgiveness is the attribute of the strong.",
                "Live as if you were to die tomorrow. Learn as if you were to live forever.",
                "First they ignore you, then they laugh at you, then they fight you, then you win.",
                "Happiness is when what you think, what you say, and what you do are in harmony.",
                "The future depends on what you do today.",
                "In a gentle way, you can shake the world.",
                "Strength does not come from physical capacity. It comes from an indomitable will.",
                "The best way to find yourself is to lose yourself in the service of others."
            ],
            ["Aristotle"] = [
                "We are what we repeatedly do. Excellence, then, is not an act but a habit.",
                "Knowing yourself is the beginning of all wisdom.",
                "The roots of education are bitter, but the fruit is sweet.",
                "Pleasure in the job puts perfection in the work.",
                "Courage is the first of human qualities because it is the quality which guarantees the others.",
                "What is a friend? A single soul dwelling in two bodies.",
                "The energy of the mind is the essence of life.",
                "Quality is not an act, it is a habit.",
                "Hope is a waking dream.",
                "The secret to humor is surprise."
            ],
            ["William Shakespeare"] = [
                "To be, or not to be, that is the question.",
                "All the world's a stage, and all the men and women merely players.",
                "This above all: to thine own self be true.",
                "Brevity is the soul of wit.",
                "Love all, trust a few, do wrong to none.",
                "We know what we are, but know not what we may be.",
                "All that glitters is not gold.",
                "Cowards die many times before their deaths; the valiant never taste of death but once.",
                "The fool doth think he is wise, but the wise man knows himself to be a fool.",
                "Love looks not with the eyes, but with the mind."
            ],
            ["Nelson Mandela"] = [
                "It always seems impossible until it's done.",
                "Education is the most powerful weapon you can use to change the world.",
                "I learned that courage was not the absence of fear, but the triumph over it.",
                "A winner is a dreamer who never gives up.",
                "May your choices reflect your hopes, not your fears.",
                "A good head and a good heart are always a formidable combination.",
                "After climbing a great hill, one only finds that there are many more hills to climb.",
                "When people are determined they can overcome anything.",
                "The greatest glory in living lies not in never falling, but in rising every time we fall.",
                "Do not judge me by my successes, judge me by how many times I fell down and got back up again."
            ],
            ["Brené Brown"] = [
                "Vulnerability is not winning or losing; it's having the courage to show up when you can't control the outcome.",
                "Courage starts with showing up and letting ourselves be seen.",
                "Authenticity is the daily practice of letting go of who we think we're supposed to be.",
                "You either walk inside your story and own it or you stand outside your story and hustle for your worthiness.",
                "Connection is why we're here. It is what gives purpose and meaning to our lives.",
                "Imperfections are not inadequacies; they are reminders that we're all in this together.",
                "Talk to yourself like someone you love.",
                "Nothing has transformed my life more than realizing that it's a waste of time to evaluate my worthiness by weighing the reaction of the people in the stands.",
                "The price of privilege is the moral obligation to act when you see another person treated unfairly.",
                "Owning our story and loving ourselves through that process is the bravest thing we will ever do."
            ]
        };

        var quotes = new List<Quote>();
        foreach (var (authorName, authorQuoteTexts) in authorQuotes)
        {
            foreach (var text in authorQuoteTexts)
            {
                var result = Quote.Create(authorName, text, now);
                if (result.IsSuccess) quotes.Add(result.Value!);
            }
        }
        // Shuffle so every page shows a mix of authors
        var rng = new Random(42);
        quotes = [.. quotes.OrderBy(_ => rng.Next())];
        db.Quotes.AddRange(quotes);
        db.SaveChanges();
    }
}

app.MapAuthEndpoints();

app.MapQuoteEndpoints();

app.MapResilienceEndpoints();

app.Run();

public partial class Program { }
