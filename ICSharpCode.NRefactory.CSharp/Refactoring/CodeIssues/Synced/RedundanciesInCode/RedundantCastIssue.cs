﻿// 
// RedundantCastIssue.cs
// 
// Author:
//      Mansheng Yang <lightyang0@gmail.com>
// 
// Copyright (c) 2012 Mansheng Yang <lightyang0@gmail.com>
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.Semantics;
using ICSharpCode.NRefactory.TypeSystem;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Refactoring;

namespace ICSharpCode.NRefactory.CSharp.Refactoring
{
	[IssueDescription ("Redundant cast",
						Description = "Type cast can be safely removed.",
						Category = IssueCategories.RedundanciesInCode,
						Severity = Severity.Warning,
						IssueMarker = IssueMarker.GrayOut,
                        ResharperDisableKeyword = "RedundantCast")]
	public class RedundantCastIssue : GatherVisitorCodeIssueProvider
	{
		protected override IGatherVisitor CreateVisitor(BaseRefactoringContext context)
		{
			return new GatherVisitor(context);
		}

		class GatherVisitor : GatherVisitorBase<RedundantCastIssue>
		{
			public GatherVisitor (BaseRefactoringContext ctx)
				: base (ctx)
			{
			}

			public override void VisitCastExpression (CastExpression castExpression)
			{
				base.VisitCastExpression (castExpression);

				CheckTypeCast (castExpression, castExpression.Expression, castExpression.StartLocation, 
					castExpression.Expression.StartLocation);
			}

			public override void VisitAsExpression (AsExpression asExpression)
			{
				base.VisitAsExpression (asExpression);

				CheckTypeCast (asExpression, asExpression.Expression, asExpression.Expression.EndLocation,
					asExpression.EndLocation);
			}

			IType GetExpectedType (Expression typeCastNode, out IMember accessingMember)
			{
				var memberRefExpr = typeCastNode.Parent as MemberReferenceExpression;
				if (memberRefExpr != null) {
					var invocationExpr = memberRefExpr.Parent as InvocationExpression;
					if (invocationExpr != null && invocationExpr.Target == memberRefExpr) {
						var invocationResolveResult = ctx.Resolve (invocationExpr) as InvocationResolveResult;
						if (invocationResolveResult != null) {
							accessingMember = invocationResolveResult.Member;
							return invocationResolveResult.Member.DeclaringType;
						}
					} else {
						var memberResolveResult = ctx.Resolve (memberRefExpr) as MemberResolveResult;
						if (memberResolveResult != null) {
							accessingMember = memberResolveResult.Member;
							return memberResolveResult.Member.DeclaringType;
						}
					}
				}
				accessingMember = null;
				return ctx.GetExpectedType (typeCastNode);
			}

			bool IsExplicitImplementation(IType exprType, IType interfaceType, Expression typeCastNode)
			{
				var memberRefExpr = typeCastNode.Parent as MemberReferenceExpression;
				if (memberRefExpr != null) {
					var rr = ctx.Resolve(memberRefExpr);
					var memberResolveResult = rr as MemberResolveResult;
					if (memberResolveResult != null) {
						foreach (var member in exprType.GetMembers (m => m.SymbolKind == memberResolveResult.Member.SymbolKind)) {
							if (member.IsExplicitInterfaceImplementation && member.ImplementedInterfaceMembers.Contains (memberResolveResult.Member)) {
								return true;
							}
						}
					}

					var methodGroupResolveResult = rr as MethodGroupResolveResult;
					if (methodGroupResolveResult != null) {
						foreach (var member in exprType.GetMethods ()) {
							if (member.IsExplicitInterfaceImplementation && member.ImplementedInterfaceMembers.Any (m => methodGroupResolveResult.Methods.Contains ((IMethod)m))) {
								return true;
							}
						}
					}
				}
				return false;
			}

			void AddIssue (Expression outerTypeCastNode, Expression typeCastNode, Expression expr, TextLocation start, TextLocation end)
			{
				AstNode type;
				if (typeCastNode is CastExpression)
					type = ((CastExpression)typeCastNode).Type;
				else 
					type = ((AsExpression)typeCastNode).Type;
				AddIssue (start, end, ctx.TranslateString ("Type cast is redundant"), string.Format(ctx.TranslateString ("Remove cast to '{0}'"), type),
				          script => script.Replace (outerTypeCastNode, expr.Clone ()));
			}

			void CheckTypeCast (Expression typeCastNode, Expression expr, TextLocation castStart, TextLocation castEnd)
			{
				var outerTypeCastNode = typeCastNode;
				while (outerTypeCastNode.Parent != null && outerTypeCastNode.Parent is ParenthesizedExpression)
					outerTypeCastNode = (Expression)outerTypeCastNode.Parent;
				IMember accessingMember;
				var expectedType = GetExpectedType (outerTypeCastNode, out accessingMember);
				var exprType = ctx.Resolve (expr).Type;
				if (expectedType.Kind == TypeKind.Interface && IsExplicitImplementation (exprType, expectedType, outerTypeCastNode))
					return;
				var baseTypes = exprType.GetAllBaseTypes().ToList();
				if (!baseTypes.Any(t => t.Equals(expectedType)))
					return;

				// check if the called member doesn't change it's virtual slot when changing types
				if (accessingMember != null) {
					var baseMember = InheritanceHelper.GetBaseMember(accessingMember);
					foreach (var bt in baseTypes) {
						foreach (var member in bt.GetMembers(m => m.Name == accessingMember.Name)) {
							if (InheritanceHelper.GetBaseMember(member) != baseMember)
								return;
						}
					}
				}
				AddIssue(outerTypeCastNode, typeCastNode, expr, castStart, castEnd);
			}
		}
	}
}
