﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenIddict.Abstractions;
using OpenIddict.EntityFramework.Models;

namespace OpenIddict.EntityFramework
{
    /// <summary>
    /// Provides methods allowing to manage the tokens stored in a database.
    /// </summary>
    /// <typeparam name="TContext">The type of the Entity Framework database context.</typeparam>
    public class OpenIddictTokenStore<TContext> : OpenIddictTokenStore<OpenIddictToken,
                                                                       OpenIddictApplication,
                                                                       OpenIddictAuthorization, TContext, string>
        where TContext : DbContext
    {
        public OpenIddictTokenStore(
            [NotNull] IMemoryCache cache,
            [NotNull] TContext context,
            [NotNull] IOptions<OpenIddictEntityFrameworkOptions> options)
            : base(cache, context, options)
        {
        }
    }

    /// <summary>
    /// Provides methods allowing to manage the tokens stored in a database.
    /// </summary>
    /// <typeparam name="TToken">The type of the Token entity.</typeparam>
    /// <typeparam name="TApplication">The type of the Application entity.</typeparam>
    /// <typeparam name="TAuthorization">The type of the Authorization entity.</typeparam>
    /// <typeparam name="TContext">The type of the Entity Framework database context.</typeparam>
    /// <typeparam name="TKey">The type of the entity primary keys.</typeparam>
    public class OpenIddictTokenStore<TToken, TApplication, TAuthorization, TContext, TKey> : IOpenIddictTokenStore<TToken>
        where TToken : OpenIddictToken<TKey, TApplication, TAuthorization>
        where TApplication : OpenIddictApplication<TKey, TAuthorization, TToken>
        where TAuthorization : OpenIddictAuthorization<TKey, TApplication, TToken>
        where TContext : DbContext
        where TKey : IEquatable<TKey>
    {
        public OpenIddictTokenStore(
            [NotNull] IMemoryCache cache,
            [NotNull] TContext context,
            [NotNull] IOptions<OpenIddictEntityFrameworkOptions> options)
        {
            Cache = cache;
            Context = context;
            Options = options;
        }

        /// <summary>
        /// Gets the memory cache associated with the current store.
        /// </summary>
        protected IMemoryCache Cache { get; }

        /// <summary>
        /// Gets the database context associated with the current store.
        /// </summary>
        protected TContext Context { get; }

        /// <summary>
        /// Gets the options associated with the current store.
        /// </summary>
        protected IOptions<OpenIddictEntityFrameworkOptions> Options { get; }

        /// <summary>
        /// Gets the database set corresponding to the <typeparamref name="TApplication"/> entity.
        /// </summary>
        private DbSet<TApplication> Applications => Context.Set<TApplication>();

        /// <summary>
        /// Gets the database set corresponding to the <typeparamref name="TAuthorization"/> entity.
        /// </summary>
        private DbSet<TAuthorization> Authorizations => Context.Set<TAuthorization>();

        /// <summary>
        /// Gets the database set corresponding to the <typeparamref name="TToken"/> entity.
        /// </summary>
        private DbSet<TToken> Tokens => Context.Set<TToken>();

        /// <summary>
        /// Determines the number of tokens that exist in the database.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the number of applications in the database.
        /// </returns>
        public virtual Task<long> CountAsync(CancellationToken cancellationToken)
            => Tokens.LongCountAsync();

        /// <summary>
        /// Determines the number of tokens that match the specified query.
        /// </summary>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the number of tokens that match the specified query.
        /// </returns>
        public virtual Task<long> CountAsync<TResult>([NotNull] Func<IQueryable<TToken>, IQueryable<TResult>> query, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return query(Tokens).LongCountAsync();
        }

        /// <summary>
        /// Creates a new token.
        /// </summary>
        /// <param name="token">The token to create.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task CreateAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            Tokens.Add(token);

            return Context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>
        /// Removes a token.
        /// </summary>
        /// <param name="token">The token to delete.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual async Task DeleteAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            Tokens.Remove(token);

            try
            {
                await Context.SaveChangesAsync(cancellationToken);
            }

            catch (DbUpdateConcurrencyException exception)
            {
                throw new OpenIddictExceptions.ConcurrencyException(new StringBuilder()
                    .AppendLine("The token was concurrently updated and cannot be persisted in its current state.")
                    .Append("Reload the token from the database and retry the operation.")
                    .ToString(), exception);
            }
        }

        /// <summary>
        /// Retrieves the tokens corresponding to the specified
        /// subject and associated with the application identifier.
        /// </summary>
        /// <param name="subject">The subject associated with the token.</param>
        /// <param name="client">The client associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the subject/client.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindAsync([NotNull] string subject,
            [NotNull] string client, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("The subject cannot be null or empty.", nameof(subject));
            }

            if (string.IsNullOrEmpty(client))
            {
                throw new ArgumentException("The client cannot be null or empty.", nameof(client));
            }

            var key = ConvertIdentifierFromString(client);

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Application != null &&
                             token.Application.Id.Equals(key) &&
                             token.Subject == subject
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves the tokens matching the specified parameters.
        /// </summary>
        /// <param name="subject">The subject associated with the token.</param>
        /// <param name="client">The client associated with the token.</param>
        /// <param name="status">The token status.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the criteria.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindAsync(
            [NotNull] string subject, [NotNull] string client,
            [NotNull] string status, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("The subject cannot be null or empty.", nameof(subject));
            }

            if (string.IsNullOrEmpty(client))
            {
                throw new ArgumentException("The client cannot be null or empty.", nameof(client));
            }

            if (string.IsNullOrEmpty(status))
            {
                throw new ArgumentException("The status cannot be null or empty.", nameof(status));
            }

            var key = ConvertIdentifierFromString(client);

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Application != null &&
                             token.Application.Id.Equals(key) &&
                             token.Subject == subject &&
                             token.Status == status
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves the tokens matching the specified parameters.
        /// </summary>
        /// <param name="subject">The subject associated with the token.</param>
        /// <param name="client">The client associated with the token.</param>
        /// <param name="status">The token status.</param>
        /// <param name="type">The token type.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the criteria.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindAsync(
            [NotNull] string subject, [NotNull] string client,
            [NotNull] string status, [NotNull] string type, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("The subject cannot be null or empty.", nameof(subject));
            }

            if (string.IsNullOrEmpty(client))
            {
                throw new ArgumentException("The client identifier cannot be null or empty.", nameof(client));
            }

            if (string.IsNullOrEmpty(status))
            {
                throw new ArgumentException("The status cannot be null or empty.", nameof(status));
            }

            if (string.IsNullOrEmpty(type))
            {
                throw new ArgumentException("The type cannot be null or empty.", nameof(type));
            }

            var key = ConvertIdentifierFromString(client);

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Application != null &&
                             token.Application.Id.Equals(key) &&
                             token.Subject == subject &&
                             token.Status == status &&
                             token.Type == type
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves the list of tokens corresponding to the specified application identifier.
        /// </summary>
        /// <param name="identifier">The application identifier associated with the tokens.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the specified application.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindByApplicationIdAsync([NotNull] string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ArgumentException("The identifier cannot be null or empty.", nameof(identifier));
            }

            var key = ConvertIdentifierFromString(identifier);

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Application != null &&
                             token.Application.Id.Equals(key)
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves the list of tokens corresponding to the specified authorization identifier.
        /// </summary>
        /// <param name="identifier">The authorization identifier associated with the tokens.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the specified authorization.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindByAuthorizationIdAsync([NotNull] string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ArgumentException("The identifier cannot be null or empty.", nameof(identifier));
            }

            var key = ConvertIdentifierFromString(identifier);

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Authorization != null &&
                             token.Authorization.Id.Equals(key)
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves a token using its unique identifier.
        /// </summary>
        /// <param name="identifier">The unique identifier associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the token corresponding to the unique identifier.
        /// </returns>
        public virtual Task<TToken> FindByIdAsync([NotNull] string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ArgumentException("The identifier cannot be null or empty.", nameof(identifier));
            }

            var key = ConvertIdentifierFromString(identifier);

            return (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                    where token.Id.Equals(key)
                    select token).FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Retrieves the list of tokens corresponding to the specified reference identifier.
        /// Note: the reference identifier may be hashed or encrypted for security reasons.
        /// </summary>
        /// <param name="identifier">The reference identifier associated with the tokens.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the specified reference identifier.
        /// </returns>
        public virtual Task<TToken> FindByReferenceIdAsync([NotNull] string identifier, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                throw new ArgumentException("The identifier cannot be null or empty.", nameof(identifier));
            }

            return (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                    where token.ReferenceId == identifier
                    select token).FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Retrieves the list of tokens corresponding to the specified subject.
        /// </summary>
        /// <param name="subject">The subject associated with the tokens.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the tokens corresponding to the specified subject.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> FindBySubjectAsync([NotNull] string subject, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("The subject cannot be null or empty.", nameof(subject));
            }

            return ImmutableArray.CreateRange(
                await (from token in Tokens.Include(token => token.Application).Include(token => token.Authorization)
                       where token.Subject == subject
                       select token).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Retrieves the optional application identifier associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the application identifier associated with the token.
        /// </returns>
        public virtual ValueTask<string> GetApplicationIdAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token.Application != null)
            {
                return new ValueTask<string>(ConvertIdentifierToString(token.Application.Id));
            }

            async Task<string> RetrieveApplicationIdAsync()
            {
                var reference = Context.Entry(token).Reference(entry => entry.Application);
                if (reference.EntityEntry.State == EntityState.Detached)
                {
                    return null;
                }

                await reference.LoadAsync(cancellationToken);

                if (token.Application == null)
                {
                    return null;
                }

                return ConvertIdentifierToString(token.Application.Id);
            }

            return new ValueTask<string>(RetrieveApplicationIdAsync());
        }

        /// <summary>
        /// Executes the specified query and returns the first element.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="state">The optional state.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the first element returned when executing the query.
        /// </returns>
        public virtual Task<TResult> GetAsync<TState, TResult>(
            [NotNull] Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
            [CanBeNull] TState state, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return query(
                Tokens.Include(token => token.Application)
                      .Include(token => token.Authorization), state).FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Retrieves the optional authorization identifier associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the authorization identifier associated with the token.
        /// </returns>
        public virtual ValueTask<string> GetAuthorizationIdAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (token.Authorization != null)
            {
                return new ValueTask<string>(ConvertIdentifierToString(token.Authorization.Id));
            }

            async Task<string> RetrieveAuthorizationIdAsync()
            {
                var reference = Context.Entry(token).Reference(entry => entry.Authorization);
                if (reference.EntityEntry.State == EntityState.Detached)
                {
                    return null;
                }

                await reference.LoadAsync(cancellationToken);

                if (token.Authorization == null)
                {
                    return null;
                }

                return ConvertIdentifierToString(token.Authorization.Id);
            }

            return new ValueTask<string>(RetrieveAuthorizationIdAsync());
        }

        /// <summary>
        /// Retrieves the creation date associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the creation date associated with the specified token.
        /// </returns>
        public virtual ValueTask<DateTimeOffset?> GetCreationDateAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<DateTimeOffset?>(token.CreationDate);
        }

        /// <summary>
        /// Retrieves the expiration date associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the expiration date associated with the specified token.
        /// </returns>
        public virtual ValueTask<DateTimeOffset?> GetExpirationDateAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<DateTimeOffset?>(token.ExpirationDate);
        }

        /// <summary>
        /// Retrieves the unique identifier associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the unique identifier associated with the token.
        /// </returns>
        public virtual ValueTask<string> GetIdAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(ConvertIdentifierToString(token.Id));
        }

        /// <summary>
        /// Retrieves the payload associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the payload associated with the specified token.
        /// </returns>
        public virtual ValueTask<string> GetPayloadAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(token.Payload);
        }

        /// <summary>
        /// Retrieves the additional properties associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns all the additional properties associated with the token.
        /// </returns>
        public virtual ValueTask<JObject> GetPropertiesAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (string.IsNullOrEmpty(token.Properties))
            {
                return new ValueTask<JObject>(new JObject());
            }

            // Note: parsing the stringified properties is an expensive operation.
            // To mitigate that, the resulting object is stored in the memory cache.
            var key = string.Concat("d0509397-1bbf-40e7-97e1-5e6d7bc2536c", "\x1e", token.Properties);
            var properties = Cache.GetOrCreate(key, entry =>
            {
                entry.SetPriority(CacheItemPriority.High)
                     .SetSlidingExpiration(TimeSpan.FromMinutes(1));

                return JObject.Parse(token.Properties);
            });

            return new ValueTask<JObject>((JObject) properties.DeepClone());
        }

        /// <summary>
        /// Retrieves the reference identifier associated with a token.
        /// Note: depending on the manager used to create the token,
        /// the reference identifier may be hashed for security reasons.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the reference identifier associated with the specified token.
        /// </returns>
        public virtual ValueTask<string> GetReferenceIdAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(token.ReferenceId);
        }

        /// <summary>
        /// Retrieves the status associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the status associated with the specified token.
        /// </returns>
        public virtual ValueTask<string> GetStatusAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(token.Status);
        }

        /// <summary>
        /// Retrieves the subject associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the subject associated with the specified token.
        /// </returns>
        public virtual ValueTask<string> GetSubjectAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(token.Subject);
        }

        /// <summary>
        /// Retrieves the token type associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the token type associated with the specified token.
        /// </returns>
        public virtual ValueTask<string> GetTypeAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            return new ValueTask<string>(token.Type);
        }

        /// <summary>
        /// Instantiates a new token.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation,
        /// whose result returns the instantiated token, that can be persisted in the database.
        /// </returns>
        public virtual ValueTask<TToken> InstantiateAsync(CancellationToken cancellationToken)
        {
            try
            {
                return new ValueTask<TToken>(Activator.CreateInstance<TToken>());
            }

            catch (MemberAccessException exception)
            {
                var source = new TaskCompletionSource<TToken>();
                source.SetException(new InvalidOperationException(new StringBuilder()
                    .AppendLine("An error occurred while trying to create a new token instance.")
                    .Append("Make sure that the token entity is not abstract and has a public parameterless constructor ")
                    .Append("or create a custom token store that overrides 'InstantiateAsync()' to use a custom factory.")
                    .ToString(), exception));

                return new ValueTask<TToken>(source.Task);
            }
        }

        /// <summary>
        /// Executes the specified query and returns all the corresponding elements.
        /// </summary>
        /// <param name="count">The number of results to return.</param>
        /// <param name="offset">The number of results to skip.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns all the elements returned when executing the specified query.
        /// </returns>
        public virtual async Task<ImmutableArray<TToken>> ListAsync(
            [CanBeNull] int? count, [CanBeNull] int? offset, CancellationToken cancellationToken)
        {
            var query = Tokens.Include(token => token.Application)
                              .Include(token => token.Authorization)
                              .OrderBy(token => token.Id)
                              .AsQueryable();

            if (offset.HasValue)
            {
                query = query.Skip(offset.Value);
            }

            if (count.HasValue)
            {
                query = query.Take(count.Value);
            }

            return ImmutableArray.CreateRange(await query.ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Executes the specified query and returns all the corresponding elements.
        /// </summary>
        /// <typeparam name="TState">The state type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <param name="query">The query to execute.</param>
        /// <param name="state">The optional state.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation,
        /// whose result returns all the elements returned when executing the specified query.
        /// </returns>
        public virtual async Task<ImmutableArray<TResult>> ListAsync<TState, TResult>(
            [NotNull] Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
            [CanBeNull] TState state, CancellationToken cancellationToken)
        {
            if (query == null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            return ImmutableArray.CreateRange(await query(
                Tokens.Include(token => token.Application)
                      .Include(token => token.Authorization), state).ToListAsync(cancellationToken));
        }

        /// <summary>
        /// Removes the tokens that are marked as expired or invalid.
        /// </summary>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual async Task PruneAsync(CancellationToken cancellationToken)
        {
            // Note: Entity Framework 6.x doesn't support set-based deletes, which prevents removing
            // entities in a single command without having to retrieve and materialize them first.
            // To work around this limitation, entities are manually listed and deleted using a batch logic.

            IList<Exception> exceptions = null;

            DbContextTransaction CreateTransaction()
            {
                // Note: relational providers like Sqlite are known to lack proper support
                // for repeatable read transactions. To ensure this method can be safely used
                // with such providers, the database transaction is created in a try/catch block.
                try
                {
                    return Context.Database.BeginTransaction(IsolationLevel.RepeatableRead);
                }

                catch
                {
                    return null;
                }
            }

            // Note: to avoid sending too many queries, the maximum number of elements
            // that can be removed by a single call to PruneAsync() is limited to 50000.
            for (var offset = 0; offset < 50_000; offset = offset + 1_000)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // To prevent concurrency exceptions from being thrown if an entry is modified
                // after it was retrieved from the database, the following logic is executed in
                // a repeatable read transaction, that will put a lock on the retrieved entries
                // and thus prevent them from being concurrently modified outside this block.
                using (var transaction = CreateTransaction())
                {
                    var tokens =
                        await (from token in Tokens
                               where token.Status != OpenIddictConstants.Statuses.Valid ||
                                     token.ExpirationDate < DateTimeOffset.UtcNow
                               orderby token.Id
                               select token).Skip(offset).Take(1_000).ToListAsync(cancellationToken);

                    if (tokens.Count == 0)
                    {
                        break;
                    }

                    Tokens.RemoveRange(tokens);

                    try
                    {
                        await Context.SaveChangesAsync(cancellationToken);
                        transaction?.Commit();
                    }

                    catch (Exception exception)
                    {
                        if (exceptions == null)
                        {
                            exceptions = new List<Exception>(capacity: 1);
                        }

                        exceptions.Add(exception);
                    }
                }
            }

            if (exceptions != null)
            {
                throw new AggregateException("An error occurred while pruning tokens.", exceptions);
            }
        }

        /// <summary>
        /// Sets the application identifier associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="identifier">The unique identifier associated with the client application.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual async Task SetApplicationIdAsync([NotNull] TToken token,
            [CanBeNull] string identifier, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (!string.IsNullOrEmpty(identifier))
            {
                var application = await Applications.FindAsync(cancellationToken, ConvertIdentifierFromString(identifier));
                if (application == null)
                {
                    throw new InvalidOperationException("The application associated with the token cannot be found.");
                }

                token.Application = application;
            }

            else
            {
                // If the application is not attached to the token, try to load it manually.
                if (token.Application == null)
                {
                    var reference = Context.Entry(token).Reference(entry => entry.Application);
                    if (reference.EntityEntry.State == EntityState.Detached)
                    {
                        return;
                    }

                    await reference.LoadAsync(cancellationToken);
                }

                token.Application = null;
            }
        }

        /// <summary>
        /// Sets the authorization identifier associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="identifier">The unique identifier associated with the authorization.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual async Task SetAuthorizationIdAsync([NotNull] TToken token,
            [CanBeNull] string identifier, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (!string.IsNullOrEmpty(identifier))
            {
                var authorization = await Authorizations.FindAsync(cancellationToken, ConvertIdentifierFromString(identifier));
                if (authorization == null)
                {
                    throw new InvalidOperationException("The authorization associated with the token cannot be found.");
                }

                token.Authorization = authorization;
            }

            else
            {
                // If the authorization is not attached to the token, try to load it manually.
                if (token.Authorization == null)
                {
                    var reference = Context.Entry(token).Reference(entry => entry.Authorization);
                    if (reference.EntityEntry.State == EntityState.Detached)
                    {
                        return;
                    }

                    await reference.LoadAsync(cancellationToken);
                }

                token.Authorization = null;
            }
        }

        /// <summary>
        /// Sets the creation date associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="date">The creation date.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetCreationDateAsync([NotNull] TToken token,
            [CanBeNull] DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            token.CreationDate = date;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the expiration date associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="date">The expiration date.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetExpirationDateAsync([NotNull] TToken token,
            [CanBeNull] DateTimeOffset? date, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            token.ExpirationDate = date;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the payload associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="payload">The payload associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetPayloadAsync([NotNull] TToken token, [CanBeNull] string payload, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            token.Payload = payload;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the additional properties associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="properties">The additional properties associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetPropertiesAsync([NotNull] TToken token, [CanBeNull] JObject properties, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (properties == null)
            {
                token.Properties = null;

                return Task.FromResult(0);
            }

            token.Properties = properties.ToString(Formatting.None);

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the reference identifier associated with a token.
        /// Note: depending on the manager used to create the token,
        /// the reference identifier may be hashed for security reasons.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="identifier">The reference identifier associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetReferenceIdAsync([NotNull] TToken token, [CanBeNull] string identifier, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            token.ReferenceId = identifier;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the status associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="status">The status associated with the authorization.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetStatusAsync([NotNull] TToken token, [CanBeNull] string status, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (string.IsNullOrEmpty(status))
            {
                throw new ArgumentException("The status cannot be null or empty.", nameof(status));
            }

            token.Status = status;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the subject associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="subject">The subject associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetSubjectAsync([NotNull] TToken token, [CanBeNull] string subject, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (string.IsNullOrEmpty(subject))
            {
                throw new ArgumentException("The subject cannot be null or empty.", nameof(subject));
            }

            token.Subject = subject;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Sets the token type associated with a token.
        /// </summary>
        /// <param name="token">The token.</param>
        /// <param name="type">The token type associated with the token.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual Task SetTokenTypeAsync([NotNull] TToken token, [CanBeNull] string type, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            if (string.IsNullOrEmpty(type))
            {
                throw new ArgumentException("The token type cannot be null or empty.", nameof(type));
            }

            token.Type = type;

            return Task.FromResult(0);
        }

        /// <summary>
        /// Updates an existing token.
        /// </summary>
        /// <param name="token">The token to update.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
        /// <returns>
        /// A <see cref="Task"/> that can be used to monitor the asynchronous operation.
        /// </returns>
        public virtual async Task UpdateAsync([NotNull] TToken token, CancellationToken cancellationToken)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            Tokens.Attach(token);

            // Generate a new concurrency token and attach it
            // to the token before persisting the changes.
            token.ConcurrencyToken = Guid.NewGuid().ToString();

            Context.Entry(token).State = EntityState.Modified;

            try
            {
                await Context.SaveChangesAsync(cancellationToken);
            }

            catch (DbUpdateConcurrencyException exception)
            {
                throw new OpenIddictExceptions.ConcurrencyException(new StringBuilder()
                    .AppendLine("The token was concurrently updated and cannot be persisted in its current state.")
                    .Append("Reload the token from the database and retry the operation.")
                    .ToString(), exception);
            }
        }

        /// <summary>
        /// Converts the provided identifier to a strongly typed key object.
        /// </summary>
        /// <param name="identifier">The identifier to convert.</param>
        /// <returns>An instance of <typeparamref name="TKey"/> representing the provided identifier.</returns>
        public virtual TKey ConvertIdentifierFromString([CanBeNull] string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
            {
                return default;
            }

            return (TKey) TypeDescriptor.GetConverter(typeof(TKey)).ConvertFromInvariantString(identifier);
        }

        /// <summary>
        /// Converts the provided identifier to its string representation.
        /// </summary>
        /// <param name="identifier">The identifier to convert.</param>
        /// <returns>A <see cref="string"/> representation of the provided identifier.</returns>
        public virtual string ConvertIdentifierToString([CanBeNull] TKey identifier)
        {
            if (Equals(identifier, default(TKey)))
            {
                return null;
            }

            return TypeDescriptor.GetConverter(typeof(TKey)).ConvertToInvariantString(identifier);
        }
    }
}