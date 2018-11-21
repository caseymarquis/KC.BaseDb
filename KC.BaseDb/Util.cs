using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace KC.BaseDb
{
    public static class Util
    {
        /// <summary>
        /// Because SQL Server has a limited date range.
        /// </summary>
        public static DateTime InitDateTime => new DateTime(1899, 1, 1);

        private static object GetMemberValue(MemberExpression member) {
            var objectMember = Expression.Convert(member, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();
            return getter();
        }

        //Kind of proud that I was able to figure this out.
        public static IQueryable<IGrouping<TKey, TSource>> GroupByDateDiff<TSource, TKey>(this IQueryable<TSource> source, Expression<Func<TSource, TKey>> keySelector) {
            var body = (NewExpression)keySelector.Body;
            foreach (var arg in body.Arguments) {
                if (arg.NodeType == ExpressionType.Call) {
                    var callNode = (MethodCallExpression)arg;
                    if (callNode.Method.Name == "DateDiff") {
                        var dateDiffFirstArg = callNode.Arguments[0];
                        if (dateDiffFirstArg.NodeType == ExpressionType.Constant) {
                            //It was already a constant, so we're good.
                        }
                        else {
                            //HACK: This will break if the internal implementation of ReadOnlyCollection changes.
                            var listInfo = typeof(ReadOnlyCollection<Expression>).GetField("list", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var list = (IList)listInfo.GetValue(callNode.Arguments);
                            if (dateDiffFirstArg.NodeType == ExpressionType.MemberAccess) {
                                list[0] = Expression.Constant((string)GetMemberValue((MemberExpression)dateDiffFirstArg));
                            }
                            else {
                                throw new ArgumentException($"{nameof(GroupByDateDiff)} was unable to parse the datePartArg argument to the DateDiff function.");
                            }
                        }
                    }
                }
            }
            return source.GroupBy(keySelector);
        }

        public class TimeSelectorSpan {
            public DateTime StartTimeUtc = Util.InitDateTime;
            public DateTime EndTimeUtc = Util.InitDateTime;
            public DateTime StartTimeLocal = Util.InitDateTime;
            public DateTime EndTimeLocal = Util.InitDateTime;
            public TimeSpan TimeZoneOffsetStart;
            public TimeSpan TimeZoneOffsetEnd;
            public string DateDiffSelector = "";
            public string UnitTime = "";

            public DateTime GetDateUtcFromBarriersCrossed(int dateDiffBarriersCrossed) {
                if (DateDiffSelector == "hh") {
                    return StartTimeUtc.AddHours(dateDiffBarriersCrossed);
                }
                else if (DateDiffSelector == "dd") {
                    return StartTimeUtc.AddDays(dateDiffBarriersCrossed);
                }
                else if (DateDiffSelector == "ww") {
                    return StartTimeUtc.AddDays(dateDiffBarriersCrossed * 7);
                }
                else if (DateDiffSelector == "mm") {
                    return Util.GetLocalStartOfUnitTimeInUtc(StartTimeUtc.AddDays(dateDiffBarriersCrossed * 30 + 15), "mm");
                }
                else {
                    throw new ArgumentException("Time Selector not supported: " + DateDiffSelector);
                }
            }
        }

        public static TimeSelectorSpan GetDateDiffSelector(DateTime startTimeUtc, DateTime endTimeUtc, string overrideDateDiffSelector = null) {
            var totalTime = endTimeUtc - startTimeUtc;
            TimeSelectorSpan span = null;
            if (overrideDateDiffSelector != null) {
                switch (overrideDateDiffSelector) {
                    case "hh":
                        span = new TimeSelectorSpan() {
                            DateDiffSelector = overrideDateDiffSelector,
                            UnitTime = "Hours"
                        };
                        break;
                    case "dd":
                        span = new TimeSelectorSpan() {
                            DateDiffSelector = overrideDateDiffSelector,
                            UnitTime = "Days"
                        };
                        break;
                    case "ww":
                        span = new TimeSelectorSpan() {
                            DateDiffSelector = overrideDateDiffSelector,
                            UnitTime = "Weeks"
                        };
                        break;
                    case "mm":
                        span = new TimeSelectorSpan() {
                            DateDiffSelector = overrideDateDiffSelector,
                            UnitTime = "Months"
                        };
                        break;
                }
            }
            else if (totalTime < new TimeSpan(0, 336, 0, 0)) { //Less than 14 days, show hours.
                span = new TimeSelectorSpan() {
                    DateDiffSelector = "hh",
                    UnitTime = "Hours",
                };
            }
            else if (totalTime < new TimeSpan(100, 0, 0, 0)) { //Less than 14 Weeks show days.
                span = new TimeSelectorSpan() {
                    DateDiffSelector = "dd",
                    UnitTime = "Days",
                };
            }
            else if (totalTime < new TimeSpan(30 * 14, 0, 0, 0)) { //Less than 14 months show weeks.
                span = new TimeSelectorSpan() {
                    DateDiffSelector = "ww",
                    UnitTime = "Weeks",
                };
            }
            else {
                span = new TimeSelectorSpan() {
                    DateDiffSelector = "mm",
                    UnitTime = "Months",
                };
            }
            span.StartTimeUtc = Util.GetLocalStartOfUnitTimeInUtc(startTimeUtc, span.DateDiffSelector);
            span.EndTimeUtc = Util.GetLocalEndOfUnitTimeInUtc(endTimeUtc, span.DateDiffSelector);
            span.StartTimeLocal = span.StartTimeUtc.ToLocalTime();
            span.EndTimeLocal = span.EndTimeUtc.ToLocalTime();
            span.TimeZoneOffsetStart = (span.StartTimeLocal - span.StartTimeUtc);
            span.TimeZoneOffsetEnd = (span.EndTimeLocal - span.EndTimeUtc);
            return span;
        }

        /// <summary>
        /// Convert the given date to local time, find the start of the hour/day/week/month/etc, convert this to UTC time, and return it.
        /// <returns></returns>
        /// </summary>
        public static DateTime GetLocalStartOfUnitTimeInUtc(DateTime startTimeUtc, string dateDiffSelector) {
            var localTime = startTimeUtc.ToLocalTime();
            if (dateDiffSelector == "hh") {
                return new DateTime(localTime.Year, localTime.Month, localTime.Day, localTime.Hour, 0, 0).ToUniversalTime();
            }
            else if (dateDiffSelector == "dd") {
                return new DateTime(localTime.Year, localTime.Month, localTime.Day, 0, 0, 0).ToUniversalTime();
            }
            var localDate = localTime.Date;
            if (dateDiffSelector == "ww") {
                return (localDate.AddDays(-(int)localDate.DayOfWeek)).ToUniversalTime();
            }
            else if (dateDiffSelector == "mm") {
                return new DateTime(localTime.Year, localTime.Month, 1).ToUniversalTime();
            }
            else {
                throw new ArgumentException("Time Selector not supported: " + dateDiffSelector);
            }
        }

        /// <summary>
        /// Convert the given date to local time, find the end of the hour/day/week/month/etc, convert this to UTC time, and return it.
        /// <returns></returns>
        /// </summary>
        public static DateTime GetLocalEndOfUnitTimeInUtc(DateTime endTimeUtc, string dateDiffSelector) {
            var start = Util.GetLocalStartOfUnitTimeInUtc(endTimeUtc, dateDiffSelector);
            if (dateDiffSelector == "hh") {
                return start.AddHours(1);
            }
            else if (dateDiffSelector == "dd") {
                return start.AddDays(1);
            }
            else if (dateDiffSelector == "ww") {
                return start.AddDays(7);
            }
            else if (dateDiffSelector == "mm") {
                return Util.GetLocalStartOfUnitTimeInUtc(start.AddDays(40), "mm");
            }
            else {
                throw new ArgumentException("Time Selector not supported: " + dateDiffSelector);
            }
        }
    }
}
