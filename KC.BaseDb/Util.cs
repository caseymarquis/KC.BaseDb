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
        public static DateTimeOffset InitDateTime => new DateTime(1899, 1, 1);

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
            public DateTimeOffset StartTime = Util.InitDateTime;
            public DateTimeOffset EndTime = Util.InitDateTime;
            public string DateDiffSelector = "";
            public string UnitTime = "";

            public DateTimeOffset GetDateUtcFromBarriersCrossed(int dateDiffBarriersCrossed) {
                if (DateDiffSelector == "hh") {
                    return StartTime.AddHours(dateDiffBarriersCrossed);
                }
                else if (DateDiffSelector == "dd") {
                    return StartTime.AddDays(dateDiffBarriersCrossed);
                }
                else if (DateDiffSelector == "ww") {
                    return StartTime.AddDays(dateDiffBarriersCrossed * 7);
                }
                else if (DateDiffSelector == "mm") {
                    return Util.GetLocalStartOfUnitTime(StartTime.AddDays(dateDiffBarriersCrossed * 30 + 15), "mm");
                }
                else {
                    throw new ArgumentException("Time Selector not supported: " + DateDiffSelector);
                }
            }
        }

        public static TimeSelectorSpan GetDateDiffSelector(DateTimeOffset startTime, DateTimeOffset endTime, string overrideDateDiffSelector = null) {
            var totalTime = endTime - startTime;
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
            span.StartTime = startTime;
            span.EndTime = endTime;
            return span;
        }

        /// <summary>
        /// Convert the given date to local time, find the start of the hour/day/week/month/etc, convert this to UTC time, and return it.
        /// <returns></returns>
        /// </summary>
        public static DateTimeOffset GetLocalStartOfUnitTime(DateTimeOffset startTime, string dateDiffSelector) {
            if (dateDiffSelector == "hh") {
                return new DateTimeOffset(new DateTime(startTime.Year, startTime.Month, startTime.Day, startTime.Hour, 0, 0), startTime.Offset);
            }
            else if (dateDiffSelector == "dd") {
                return  new DateTimeOffset(new DateTime(startTime.Year, startTime.Month, startTime.Day, 0, 0, 0), startTime.Offset);
            }
            var localDate = startTime.Date;
            if (dateDiffSelector == "ww") {
                return (localDate.AddDays(-(int)localDate.DayOfWeek));
            }
            else if (dateDiffSelector == "mm") {
                return  new DateTimeOffset(new DateTime(startTime.Year, startTime.Month, 1), startTime.Offset);
            }
            else {
                throw new ArgumentException("Time Selector not supported: " + dateDiffSelector);
            }
        }

        /// <summary>
        /// Convert the given date to local time, find the end of the hour/day/week/month/etc, convert this to UTC time, and return it.
        /// <returns></returns>
        /// </summary>
        public static DateTimeOffset GetLocalEndOfUnitTime(DateTime endTime, string dateDiffSelector) {
            var start = Util.GetLocalStartOfUnitTime(endTime, dateDiffSelector);
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
                return Util.GetLocalStartOfUnitTime(start.AddDays(40), "mm");
            }
            else {
                throw new ArgumentException("Time Selector not supported: " + dateDiffSelector);
            }
        }
    }
}
