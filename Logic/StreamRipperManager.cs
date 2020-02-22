﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Logic.Interfaces;
using Logic.Models;
using Microsoft.Extensions.Logging;
using Models.Enums;
using Models.Models;
using StreamRipper;
using StreamRipper.Interfaces;
using Stream = Models.Models.Stream;

namespace Logic
{
    public class StreamRipperManager : IStreamRipperManager
    {
        private readonly IStreamLogic _streamLogic;

        private readonly ISinkService _sinkService;

        private readonly StreamRipperState _state;
        
        private readonly ILogger<IStreamRipper> _logger;

        /// <summary>
        /// Constructor dependency injection
        /// </summary>
        /// <param name="state"></param>
        /// <param name="streamLogic"></param>
        /// <param name="sinkService"></param>
        /// <param name="logger"></param>
        public StreamRipperManager(StreamRipperState state, IStreamLogic streamLogic, ISinkService sinkService, ILogger<IStreamRipper> logger)
        {
            _state = state;
            _streamLogic = streamLogic;
            _sinkService = sinkService;
            _logger = logger;
        }

        public IStreamRipperManagerImpl For(User user)
        {
            return new StreamRipperManagerImpl(_state, _streamLogic, _sinkService, user, _logger);
        }
    }

    public class StreamRipperManagerImpl : IStreamRipperManagerImpl
    {
        private readonly IStreamLogic _streamLogic;

        private readonly ISinkService _sinkService;

        private readonly StreamRipperState _state;
        
        private readonly User _user;
        
        private readonly ILogger<IStreamRipper> _logger;

        /// <summary>
        /// Constructor dependency injection
        /// </summary>
        /// <param name="state"></param>
        /// <param name="streamLogic"></param>
        /// <param name="sinkService"></param>
        /// <param name="user"></param>
        /// <param name="logger"></param>
        public StreamRipperManagerImpl(StreamRipperState state, IStreamLogic streamLogic, ISinkService sinkService, User user, ILogger<IStreamRipper> logger)
        {
            _state = state;
            _streamLogic = streamLogic;
            _sinkService = sinkService;
            _user = user;
            _logger = logger;
        }
        
        /// <summary>
        /// Pass username to GetAll
        /// </summary>
        /// <returns></returns>
        public async Task<Dictionary<int, StreamStatusEnum>> Status()
        {
            var streams = await _streamLogic.For(_user).GetAll();

            return streams
                .ToDictionary(x => x.Id,
                    x => _state.StreamItems.FirstOrDefault(y => y.Value.User.Id == _user.Id).Value?.State ??
                         StreamStatusEnum.Stopped);
        }

        /// <summary>
        /// Start the stream
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> Start(int id)
        {
            Stream stream;
            
            // Already started
            if (_state.StreamItems.ContainsKey(id) || (stream = await _streamLogic.For(_user).Get(id)) == null)
            {
                return false;
            }

            var streamRipperInstance = new StreamRipperImpl(new Uri(stream.Url), _logger);

            var aggregatedSink = await _sinkService.Resolve(stream);

            streamRipperInstance.SongChangedEventHandlers += async (_, arg) =>
            {
                // Needed
                arg.SongInfo.Stream.Seek(0, SeekOrigin.Begin);

                // Create filename
                var filename = $"{arg.SongInfo.SongMetadata.Artist}-{arg.SongInfo.SongMetadata.Title}";

                // Upload the stream
                await aggregatedSink(arg.SongInfo.Stream, $"{filename}.mp3");
            };

            streamRipperInstance.StreamEndedEventHandlers += (sender, arg) =>
            {
                _state.StreamItems[id].State = StreamStatusEnum.Stopped;
            };

            streamRipperInstance.StreamFailedHandlers += (sender, arg) =>
            {
                _state.StreamItems[id].State = StreamStatusEnum.Fail;
            };

            // Start the ripper
            streamRipperInstance.Start();

            // Add to the dictionary
            _state.StreamItems[id] = new StreamItem
            {
                User = _user,
                StreamRipper = streamRipperInstance,
                State = StreamStatusEnum.Started
            };

            return true;
        }

        /// <summary>
        /// Stop the stream given id
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<bool> Stop(int id)
        {
            if (_state.StreamItems.ContainsKey(id) && _state.StreamItems[id].User.Id == _user.Id)
            {
                _state.StreamItems[id].StreamRipper.Dispose();

                _state.StreamItems.Remove(id);

                return true;
            }

            return false;
        }
    }
}