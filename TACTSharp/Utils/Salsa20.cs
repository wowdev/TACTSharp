﻿using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace TACTSharp
{
    /// <summary>
    /// Implements the Salsa20 stream encryption cipher, as defined at http://cr.yp.to/snuffle.html.
    /// </summary>
    /// <remarks>See <a href="https://faithlife.codes/blog/2008/06/salsa20_implementation_in_c_1/">Salsa20 Implementation in C#</a>.</remarks>
    public sealed class Salsa20
    {
        /// <summary>
        /// Creates a symmetric decryptor object with the specified <see cref="SymmetricAlgorithm.Key"/> property
        /// and initialization vector (<see cref="SymmetricAlgorithm.IV"/>).
        /// </summary>
        /// <param name="rgbKey">The secret key to use for the symmetric algorithm.</param>
        /// <param name="rgbIV">The initialization vector to use for the symmetric algorithm.</param>
        /// <param name="offset">The offset to start decryption.</param>
        /// <returns>A symmetric decryptor object.</returns>
        public Salsa20CryptoTransform CreateDecryptor(ReadOnlySpan<byte> rgbKey, ReadOnlySpan<byte> rgbIV, int offset = 0)
        {
            // decryption and encryption are symmetrical
            var decryptor = CreateEncryptor(rgbKey, rgbIV);
            if (offset != 0)
                (decryptor as Salsa20CryptoTransform).SetOffset(offset);

            return decryptor;
        }

        /// <summary>
        /// Creates a symmetric encryptor object with the specified <see cref="SymmetricAlgorithm.Key"/> property
        /// and initialization vector (<see cref="SymmetricAlgorithm.IV"/>).
        /// </summary>
        /// <param name="rgbKey">The secret key to use for the symmetric algorithm.</param>
        /// <param name="rgbIV">The initialization vector to use for the symmetric algorithm.</param>
        /// <returns>A symmetric encryptor object.</returns>
        public Salsa20CryptoTransform CreateEncryptor(ReadOnlySpan<byte> rgbKey, ReadOnlySpan<byte> rgbIV)
        {
            if (rgbKey.IsEmpty)
                throw new ArgumentNullException(nameof(rgbKey));
            if (!ValidKeySize(rgbKey.Length * 8))
                throw new CryptographicException("Invalid key size; it must be 128 or 256 bits.");
            CheckValidIV(rgbIV, nameof(rgbIV));

            return new Salsa20CryptoTransform(rgbKey, rgbIV, 20);
        }

        private static bool ValidKeySize(int size)
        {
            return size == 128 || size == 256;
        }

        // Verifies that iv is a legal value for a Salsa20 IV.
        private static void CheckValidIV(ReadOnlySpan<byte> iv, string paramName)
        {
            if (iv.IsEmpty)
                throw new ArgumentNullException(paramName);
            if (iv.Length != 8)
                throw new CryptographicException("Invalid IV size; it must be 8 bytes.");
        }

        /// <summary>
        /// Salsa20Impl is an implementation of <see cref="ICryptoTransform"/> that uses the Salsa20 algorithm.
        /// </summary>
        public sealed class Salsa20CryptoTransform
        {
            public Salsa20CryptoTransform(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, int rounds)
            {
                Debug.Assert(key.Length == 16 || key.Length == 32, "abyKey.Length == 16 || abyKey.Length == 32", "Invalid key size.");
                Debug.Assert(iv.Length == 8, "abyIV.Length == 8", "Invalid IV size.");
                Debug.Assert(rounds == 8 || rounds == 12 || rounds == 20, "rounds == 8 || rounds == 12 || rounds == 20", "Invalid number of rounds.");

                m_state = new uint[16];
                m_state[1] = ToUInt32(key, 0);
                m_state[2] = ToUInt32(key, 4);
                m_state[3] = ToUInt32(key, 8);
                m_state[4] = ToUInt32(key, 12);

                ReadOnlySpan<byte> constants = key.Length == 32 ? c_sigma : c_tau;
                int keyIndex = key.Length - 16;

                m_state[11] = ToUInt32(key, keyIndex + 0);
                m_state[12] = ToUInt32(key, keyIndex + 4);
                m_state[13] = ToUInt32(key, keyIndex + 8);
                m_state[14] = ToUInt32(key, keyIndex + 12);
                m_state[0] = ToUInt32(constants, 0);
                m_state[5] = ToUInt32(constants, 4);
                m_state[10] = ToUInt32(constants, 8);
                m_state[15] = ToUInt32(constants, 12);

                m_state[6] = ToUInt32(iv, 0);
                m_state[7] = ToUInt32(iv, 4);
                m_state[8] = 0;
                m_state[9] = 0;

                m_rounds = rounds;
            }

            public bool CanReuseTransform
            {
                get { return false; }
            }

            public bool CanTransformMultipleBlocks
            {
                get { return true; }
            }

            public int InputBlockSize
            {
                get { return 64; }
            }

            public int OutputBlockSize
            {
                get { return 64; }
            }

            public int TransformBlock(ReadOnlySpan<byte> inputBuffer, int inputOffset, int inputCount, byte[] outputBuffer, int outputOffset)
            {
                // check arguments
                if (inputBuffer.IsEmpty)
                    throw new ArgumentNullException(nameof(inputBuffer));
                if (inputOffset < 0 || inputOffset >= inputBuffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(inputOffset));
                if (inputCount < 0 || inputOffset + inputCount > inputBuffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(inputCount));

                ArgumentNullException.ThrowIfNull(outputBuffer);

                if (outputOffset < 0 || outputOffset + inputCount > outputBuffer.Length)
                    throw new ArgumentOutOfRangeException(nameof(outputOffset));
                ObjectDisposedException.ThrowIf(m_state == null, this);

                byte[] output = new byte[64];
                int bytesTransformed = 0;

                while (inputCount > 0)
                {
                    Hash(output, m_state);

                    int blockSize = Math.Min(64 - m_decryptOffset, inputCount);
                    for (int i = 0; i < blockSize; i++)
                        outputBuffer[outputOffset + i] = (byte)(inputBuffer[inputOffset + i] ^ output[i + m_decryptOffset]);
                    bytesTransformed += blockSize;

                    inputCount -= blockSize;
                    outputOffset += blockSize;
                    inputOffset += blockSize;

                    m_decryptOffset = (m_decryptOffset + blockSize) % 64;

                    // if m_decryptStart is 0, it means we reached the border of the block
                    // and the next decrypt part will begin from the next block
                    if (m_decryptOffset == 0)
                    {
                        m_state[8] = AddOne(m_state[8]);
                        if (m_state[8] == 0)
                        {
                            // NOTE: stopping at 2^70 bytes per nonce is user's responsibility
                            m_state[9] = AddOne(m_state[9]);
                        }
                    }
                }

                return bytesTransformed;
            }

            public byte[] TransformFinalBlock(ReadOnlySpan<byte> inputBuffer, int inputOffset, int inputCount)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(inputCount);

                byte[] output = new byte[inputCount];
                TransformBlock(inputBuffer, inputOffset, inputCount, output, 0);
                return output;
            }

            public void SetOffset(int offset)
            {
                m_state[8] = (uint)(offset / 64);
                m_decryptOffset = offset % 64;
            }

            private static uint Rotate(uint v, int c)
            {
                return (v << c) | (v >> (32 - c));
            }

            private static uint Add(uint v, uint w)
            {
                return unchecked(v + w);
            }

            private static uint AddOne(uint v)
            {
                return unchecked(v + 1);
            }

            private void Hash(Span<byte> output, ReadOnlySpan<uint> input)
            {
                uint[] state = input.ToArray();

                for (int round = m_rounds; round > 0; round -= 2)
                {
                    state[4] ^= Rotate(Add(state[0], state[12]), 7);
                    state[8] ^= Rotate(Add(state[4], state[0]), 9);
                    state[12] ^= Rotate(Add(state[8], state[4]), 13);
                    state[0] ^= Rotate(Add(state[12], state[8]), 18);
                    state[9] ^= Rotate(Add(state[5], state[1]), 7);
                    state[13] ^= Rotate(Add(state[9], state[5]), 9);
                    state[1] ^= Rotate(Add(state[13], state[9]), 13);
                    state[5] ^= Rotate(Add(state[1], state[13]), 18);
                    state[14] ^= Rotate(Add(state[10], state[6]), 7);
                    state[2] ^= Rotate(Add(state[14], state[10]), 9);
                    state[6] ^= Rotate(Add(state[2], state[14]), 13);
                    state[10] ^= Rotate(Add(state[6], state[2]), 18);
                    state[3] ^= Rotate(Add(state[15], state[11]), 7);
                    state[7] ^= Rotate(Add(state[3], state[15]), 9);
                    state[11] ^= Rotate(Add(state[7], state[3]), 13);
                    state[15] ^= Rotate(Add(state[11], state[7]), 18);
                    state[1] ^= Rotate(Add(state[0], state[3]), 7);
                    state[2] ^= Rotate(Add(state[1], state[0]), 9);
                    state[3] ^= Rotate(Add(state[2], state[1]), 13);
                    state[0] ^= Rotate(Add(state[3], state[2]), 18);
                    state[6] ^= Rotate(Add(state[5], state[4]), 7);
                    state[7] ^= Rotate(Add(state[6], state[5]), 9);
                    state[4] ^= Rotate(Add(state[7], state[6]), 13);
                    state[5] ^= Rotate(Add(state[4], state[7]), 18);
                    state[11] ^= Rotate(Add(state[10], state[9]), 7);
                    state[8] ^= Rotate(Add(state[11], state[10]), 9);
                    state[9] ^= Rotate(Add(state[8], state[11]), 13);
                    state[10] ^= Rotate(Add(state[9], state[8]), 18);
                    state[12] ^= Rotate(Add(state[15], state[14]), 7);
                    state[13] ^= Rotate(Add(state[12], state[15]), 9);
                    state[14] ^= Rotate(Add(state[13], state[12]), 13);
                    state[15] ^= Rotate(Add(state[14], state[13]), 18);
                }

                for (int index = 0; index < 16; index++)
                    ToBytes(Add(state[index], input[index]), output, 4 * index);
            }

            private static uint ToUInt32(ReadOnlySpan<byte> input, int inputOffset)
            {
                return unchecked((uint)(((input[inputOffset] | (input[inputOffset + 1] << 8)) | (input[inputOffset + 2] << 16)) | (input[inputOffset + 3] << 24)));
            }

            private static void ToBytes(uint input, Span<byte> output, int outputOffset)
            {
                unchecked
                {
                    output[outputOffset] = (byte)input;
                    output[outputOffset + 1] = (byte)(input >> 8);
                    output[outputOffset + 2] = (byte)(input >> 16);
                    output[outputOffset + 3] = (byte)(input >> 24);
                }
            }

            static readonly byte[] c_sigma = Encoding.ASCII.GetBytes("expand 32-byte k");
            static readonly byte[] c_tau = Encoding.ASCII.GetBytes("expand 16-byte k");

            readonly uint[] m_state;
            readonly int m_rounds;
            int m_decryptOffset;
        }
    }
}