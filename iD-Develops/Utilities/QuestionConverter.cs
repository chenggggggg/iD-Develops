using iD_Develops.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace iD_Develops.Utilities
{
    public class QuestionConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(Question).IsAssignableFrom(objectType);
        }

#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
#pragma warning restore CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        {
            var jsonObject = JObject.Load(reader);

            var questionType = jsonObject["QuestionType"]?.ToString();

            Question question = questionType switch
            {
                "multipleChoice" => new MultipleChoiceQuestion(),
                "open" => new OpenQuestion(),
                "trueFalse" => new TrueOrFalseQuestion(),
                _ => throw new JsonSerializationException("Unable to determine the question type.")
            };

            // Manually handle CorrectAnswers
            var correctAnswers = new List<CorrectAnswer>();
            var index = 0;

            while (true)
            {
                var textProperty = $"CorrectAnswers[{index}].Text";
                var scoreProperty = $"CorrectAnswers[{index}].Score";

                var text = jsonObject[textProperty]?.ToString();
                var scoreString = jsonObject[scoreProperty]?.ToString();

                if (text == null)
                    break;

                double? score = null;
                if (!string.IsNullOrEmpty(scoreString))
                {
                    if (double.TryParse(scoreString, out var parsedScore))
                    {
                        score = parsedScore;
                    }
                    else
                    {
                        throw new JsonSerializationException($"Score value '{scoreString}' is not in a correct format.");
                    }
                }

                correctAnswers.Add(new CorrectAnswer
                {
                    Text = text,
                    Score = score
                });

                index++;
            }

            question.CorrectAnswers = correctAnswers;

            // Populate the rest of the Question properties
            serializer.Populate(jsonObject.CreateReader(), question);
            return question;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }

}