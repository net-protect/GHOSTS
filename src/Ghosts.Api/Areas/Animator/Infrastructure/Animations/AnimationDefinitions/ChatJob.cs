// Copyright 2017 Carnegie Mellon University. All Rights Reserved. See LICENSE.md file for terms.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Ghosts.Animator.Extensions;
using ghosts.api.Areas.Animator.Hubs;
using ghosts.api.Areas.Animator.Infrastructure.Animations.AnimationDefinitions.Chat;
using ghosts.api.Areas.Animator.Infrastructure.ContentServices.Ollama;
using ghosts.api.Areas.Animator.Infrastructure.Models;
using Ghosts.Api.Infrastructure;
using Ghosts.Api.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using NLog;

namespace ghosts.api.Areas.Animator.Infrastructure.Animations.AnimationDefinitions;

public class ChatJob
{
    private static readonly Logger _log = LogManager.GetCurrentClassLogger();
    private readonly ApplicationSettings _configuration;
    private readonly ApplicationDbContext _context;
    private readonly Random _random;
    private readonly ChatClient _chatClient;
    private readonly int _currentStep;
    private CancellationToken _cancellationToken;
    
    public ChatJob(ApplicationSettings configuration, ApplicationDbContext context, Random random,
        IHubContext<ActivityHub> activityHubContext, CancellationToken cancellationToken)
    {
        //todo: post results to activityHubContext for "top" reporting
        
        this._configuration = configuration;
        this._random = random;
        this._context = context;
        this._cancellationToken = cancellationToken;

        var chatConfiguration = JsonSerializer.Deserialize<ChatJobConfiguration>(File.ReadAllText("config/chat.json"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException();

        var llm = new OllamaConnectorService(_configuration.AnimatorSettings.Animations.Chat.ContentEngine);
        this._chatClient = new ChatClient(chatConfiguration);
        
        while (!_cancellationToken.IsCancellationRequested)
        {
            if (this._currentStep > _configuration.AnimatorSettings.Animations.Chat.MaximumSteps)
            {
                _log.Trace($"Maximum steps met: {this._currentStep - 1}. Chat Job is exiting...");
                return;
            }

            this.Step(llm, random, chatConfiguration);
            Thread.Sleep(this._configuration.AnimatorSettings.Animations.SocialSharing.TurnLength);

            this._currentStep++;
        }
    }

    private async void Step(OllamaConnectorService llm, Random random, ChatJobConfiguration chatConfiguration)
    {
        _log.Trace("Executing a chat step...");
        var agents = this._context.Npcs.ToList().Shuffle(_random).Take(chatConfiguration.Chat.AgentsPerBatch);
        await this._chatClient.Step(llm, random, agents);
    }
}