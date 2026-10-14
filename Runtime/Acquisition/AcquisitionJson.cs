using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pludibase
{
    internal static class AcquisitionJson
    {
        internal static string Utc(DateTime value) { return value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture); }

        internal static string Quote(string value)
        {
            var text = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 32 || char.IsSurrogate(c)) text.Append("\\u").Append(((int)c).ToString("x4"));
                        else text.Append(c);
                        break;
                }
            }
            return text.Append('"').ToString();
        }

        private static StringBuilder Identity(AcquisitionState state)
        {
            return new StringBuilder("{\"installId\":").Append(Quote(state.installId))
                .Append(",\"installationSecret\":").Append(Quote(state.installationSecret));
        }

        internal static string FirstOpen(AcquisitionState state)
        {
            var text = Identity(state).Append(",\"eventId\":").Append(Quote(state.firstOpenEventId))
                .Append(",\"occurredAt\":").Append(Quote(state.firstOpenAt))
                .Append(",\"platform\":").Append(Quote(state.platform));
            if (!string.IsNullOrEmpty(state.appVersion)) text.Append(",\"appVersion\":").Append(Quote(state.appVersion));
            if (!string.IsNullOrEmpty(state.buildVersion)) text.Append(",\"buildVersion\":").Append(Quote(state.buildVersion));
            if (!string.IsNullOrEmpty(state.country)) text.Append(",\"country\":").Append(Quote(state.country));
            return text.Append('}').ToString();
        }

        internal static string Events(AcquisitionState state, IList<AcquisitionRecord> records)
        {
            var text = Identity(state).Append(",\"events\":[");
            for (int i = 0; i < records.Count; i++)
            {
                if (i > 0) text.Append(',');
                text.Append(Event(records[i]));
            }
            return text.Append("]}").ToString();
        }

        internal static string Event(AcquisitionRecord record)
        {
            var text = new StringBuilder("{\"eventId\":").Append(Quote(record.eventId)).Append(",\"name\":").Append(Quote(record.name))
                .Append(",\"occurredAt\":").Append(Quote(record.occurredAt)).Append(",\"props\":{");
            for (int p = 0; p < record.props.Count; p++)
            {
                if (p > 0) text.Append(',');
                text.Append(Quote(record.props[p].key)).Append(':').Append(Quote(record.props[p].value));
            }
            return text.Append("}}").ToString();
        }

        internal static string Attribution(AcquisitionState state, AcquisitionRecord record)
        {
            var text = Identity(state).Append(",\"eventId\":").Append(Quote(record.eventId))
                .Append(",\"occurredAt\":").Append(Quote(record.occurredAt))
                .Append(",\"source\":").Append(Quote(record.source)).Append(",\"rawReferrer\":").Append(Quote(record.rawReferrer));
            if (!string.IsNullOrEmpty(record.referrerClickAt)) text.Append(",\"referrerClickAt\":").Append(Quote(record.referrerClickAt));
            if (!string.IsNullOrEmpty(record.installBeginAt)) text.Append(",\"installBeginAt\":").Append(Quote(record.installBeginAt));
            return text.Append('}').ToString();
        }

        internal static string Link(AcquisitionState state) { return Identity(state).Append('}').ToString(); }
    }
}
